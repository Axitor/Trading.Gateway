using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UserDataStream.ListenKey;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.RateLimiter;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>A user data session bound to its handler, transport and listen key endpoint.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Each event is its own subscription, "listenKey@EVENT", so the exchange sends only those events.
/// A bare listenKey is never used: with both on one connection, every event comes twice.
/// </para>
/// <para>
/// DESIGN NOTE: COIN-M documents only /ws/listenKey, but dstream takes the same "listenKey@EVENT" subscriptions on /stream
/// (checked live, Oct 2026), so one session serves both futures markets.
/// </para>
/// <para>
/// DESIGN NOTE: "listenKey@listenKeyExpired" is always subscribed. With an event filter the exchange sends the expiry
/// only when asked for it, and the socket stays open: without it the session would go blind with no signal.
/// </para>
/// <para>
/// DESIGN NOTE: A SUBSCRIBE with a dead key still answers success, and no data ever comes.
/// So the key is checked after every reconnect: if it died while we were offline, its expiry event was lost with the connection.
/// </para>
/// <para>
/// DESIGN NOTE: The key is checked with userDataStream.start, not ping. On a live key it returns the same key and extends it,
/// on a dead one it returns a new key. One call does both; a different key means the old one died.
/// </para>
/// <para>
/// DESIGN NOTE: The key is never closed with userDataStream.stop. It is shared by every app on the account,
/// and closing it stops their user data too.
/// </para>
/// <para>
/// DESIGN NOTE: The expiry subscription is the anchor of the session: only its breaks reach the handler, one per gap.
/// The handler hears that data flows again only after the key is checked, so a gap is reported once, with its real start.
/// </para>
/// <para>
/// DESIGN NOTE: One connection per session, so events of different types keep the order the exchange sent them in,
/// e.g. a fill before the position change it caused.
/// </para>
/// <para>
/// DESIGN NOTE: Subscribe answers only Subscribed or Rejected. Without an answer the exchange may or may not hold the subscription,
/// and a bot that loads its orders after such a maybe would miss events with no signal. So it starts over until the exchange confirms.
/// A canceled subscribe is undone: nothing of it stays registered.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The semaphore holds no unmanaged state without a wait handle; the loop source is disposed when the loop stops.")]
internal sealed class BinanceUserDataSessionCore<THandler, TTelemetry> : BinanceUserDataSession
    where THandler : struct, IBinanceUserDataHandler
    where TTelemetry : struct, ITelemetryHandler
{
    private static readonly BinanceUserDataEvents[] _supportedEvents =
    [
        BinanceUserDataEvents.OrderTradeUpdate,
        BinanceUserDataEvents.AccountUpdate,
        BinanceUserDataEvents.TradeLite,
        BinanceUserDataEvents.AlgoUpdate,
        BinanceUserDataEvents.AccountConfigUpdate,
        BinanceUserDataEvents.MarginCall,
    ];

    private readonly WsStreamTransport<BinanceDialectWsStream, BinanceRateLimiterRef, BinanceUserDataDispatcher<THandler>, TTelemetry> _transport;
    private readonly BinanceUserDataState _state;
    private readonly ListenKeyEndpoint<TTelemetry> _listenKey;
    private readonly THandler _handler;
    private readonly BinanceUserDataEvents _marketEvents;
    private readonly TimeSpan _keepAliveInterval;
    private readonly int _retryBaseDelayMillis;
    private readonly int _retryMaxDelayMillis;
    private readonly int _minJitterMillis;
    private readonly int _maxJitterMillis;
    private readonly CancellationToken _appToken;

    // One subscription change at a time: subscribe, unsubscribe, key check.
    private readonly SemaphoreSlim _controlLock = new(1, 1);

    // Handles of the current subscriptions, -1 when none. Touched under the control lock.
    private readonly int[] _eventHandles;
    private int _anchorHandle = -1;
    private BinanceUserDataEvents _events;
    private bool _subscribed;
    private int _needsResubscribe;
    private CancellationTokenSource? _loopSource;

    public BinanceUserDataSessionCore(
        WsStreamTransport<BinanceDialectWsStream, BinanceRateLimiterRef, BinanceUserDataDispatcher<THandler>, TTelemetry> transport,
        BinanceUserDataState state,
        ListenKeyEndpoint<TTelemetry> listenKey,
        THandler handler,
        BinanceUserDataEvents marketEvents,
        TimeSpan keepAliveInterval,
        ResiliencyWsStreamOptions resiliency,
        CancellationToken appToken)
    {
        _transport = transport;
        _state = state;
        _listenKey = listenKey;
        _handler = handler;
        _marketEvents = marketEvents;
        _keepAliveInterval = keepAliveInterval;
        _retryBaseDelayMillis = resiliency.ReconnectBaseDelayMillis;
        _retryMaxDelayMillis = resiliency.ReconnectMaxDelayMillis;
        _minJitterMillis = resiliency.MinJitterMillisOnReconnect;
        _maxJitterMillis = resiliency.MaxJitterMillisOnReconnect;
        _appToken = appToken;

        _eventHandles = new int[_supportedEvents.Length];
        Array.Fill(_eventHandles, -1);
    }

    /// <inheritdoc />
    public override Task Completion => _transport.Completion;

    /// <inheritdoc />
    public override async ValueTask<WsStreamSubscribeStatus> SubscribeAsync(BinanceUserDataEvents events, CancellationToken cancellationToken)
    {
        // The exchange takes a subscription to an event it never sends, so it would stay silent for good.
        if ((events & ~_marketEvents) != 0)
        {
            ThrowEventsNotSupported(events);
        }

        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_subscribed)
            {
                return WsStreamSubscribeStatus.AlreadySubscribed;
            }

            _events = events;

            if (!await SubscribeUntilConfirmedLockedAsync(cancellationToken).ConfigureAwait(false))
            {
                _events = BinanceUserDataEvents.None;
                return WsStreamSubscribeStatus.Rejected;
            }

            Volatile.Write(ref _needsResubscribe, 0);
            _subscribed = true;

            _loopSource = CancellationTokenSource.CreateLinkedTokenSource(_appToken);
            _ = RunKeyLoopAsync(_loopSource.Token);

            return WsStreamSubscribeStatus.Subscribed;
        }
        catch (OperationCanceledException)
        {
            await RollBackLockedAsync().ConfigureAwait(false);
            throw;
        }
        catch (WsApiFatalException exception)
        {
            EnterFatal(exception);
            throw;
        }
        finally
        {
            _controlLock.Release();
        }
    }

    /// <inheritdoc />
    public override async ValueTask<bool> UnsubscribeAsync(CancellationToken cancellationToken)
    {
        await _controlLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_subscribed)
            {
                return false;
            }

            _loopSource?.Cancel();
            _loopSource?.Dispose();
            _loopSource = null;

            bool confirmed = await UnsubscribeAllLockedAsync(cancellationToken).ConfigureAwait(false);

            _subscribed = false;
            _events = BinanceUserDataEvents.None;
            Volatile.Write(ref _needsResubscribe, 0);

            return confirmed;
        }
        finally
        {
            _controlLock.Release();
        }
    }

    /// <summary>Keeps the key alive and closes gaps once the key is checked. Runs while subscribed.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A handler exception in a gap event is a bug and turns the session fatal.")]
    private async Task RunKeyLoopAsync(CancellationToken loopToken)
    {
        bool expired = false;
        bool reconnected = false;
        int failures = 0;

        try
        {
            while (true)
            {
                int attempt = Volatile.Read(ref _needsResubscribe) == 1 ? Math.Max(1, failures) : failures;
                TimeSpan wait = attempt == 0 ? _keepAliveInterval : TimeSpan.FromMilliseconds(GetBackoffDelayMillis(attempt));

                await _state.WaitAsync(wait, loopToken).ConfigureAwait(false);

                // Kept until a check succeeds, so a failed one is retried with the same cause.
                expired |= _state.TakeExpired();
                reconnected |= _state.TakeReconnected();

                await _controlLock.WaitAsync(loopToken).ConfigureAwait(false);

                try
                {
                    if (await CheckKeyLockedAsync(expired, reconnected).ConfigureAwait(false))
                    {
                        failures = 0;
                        expired = false;
                        reconnected = false;
                    }
                    else
                    {
                        failures++;
                    }
                }
                finally
                {
                    _controlLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Unsubscribed or app shutdown.
        }
        catch (WsStreamFatalException)
        {
            // The transport has already told the handler.
        }
        catch (WsApiFatalException exception)
        {
            EnterFatal(exception);
        }
        catch (Exception exception)
        {
            // Handlers must never throw: this is a bug, and retrying cannot fix it.
            WsDisconnectInfo fault = new() { Reason = WsDisconnectReason.DispatchFault, Fault = exception };
            _transport.EnterFatal(in fault);
        }
    }

    /// <summary>Checks and extends the key, moves to a new one when needed and closes the gap. False when it should be tried again.</summary>
    private async ValueTask<bool> CheckKeyLockedAsync(bool expired, bool reconnected)
    {
        byte[]? key = await StartKeyAsync(_appToken).ConfigureAwait(false);

        if (key is null)
        {
            return false;
        }

        bool changed = !key.AsSpan().SequenceEqual(_state.ListenKey);

        if (expired || changed || Volatile.Read(ref _needsResubscribe) == 1)
        {
            // After a reconnect the gap is already open with its real cause and start.
            OpenGap(WsDisconnectReason.SubscriptionExpired);

            await UnsubscribeAllLockedAsync(_appToken).ConfigureAwait(false);
            _state.ListenKey = key;

            WsStreamSubscribeStatus status = await SubscribeAllLockedAsync(key, _appToken).ConfigureAwait(false);
            bool confirmed = status == WsStreamSubscribeStatus.Subscribed;
            Volatile.Write(ref _needsResubscribe, confirmed ? 0 : 1);

            if (!confirmed)
            {
                return false;
            }
        }
        else if (!reconnected)
        {
            // Keep-alive only: the key is extended.
            return true;
        }

        if (!_transport.IsFatal)
        {
            CloseGap();
        }

        return true;
    }

    /// <summary>Takes the key and subscribes every event until the exchange confirms them all. False when it refused. Called under the control lock.</summary>
    private async ValueTask<bool> SubscribeUntilConfirmedLockedAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (0 < attempt)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(GetBackoffDelayMillis(attempt)), cancellationToken).ConfigureAwait(false);
            }

            byte[]? key = await StartKeyAsync(cancellationToken).ConfigureAwait(false);

            if (key is null)
            {
                // A canceled key request ends as a failed result, not as an exception.
                cancellationToken.ThrowIfCancellationRequested();
                _appToken.ThrowIfCancellationRequested();
                return false;
            }

            _state.ListenKey = key;

            WsStreamSubscribeStatus status = await SubscribeAllLockedAsync(key, cancellationToken).ConfigureAwait(false);

            if (status == WsStreamSubscribeStatus.Subscribed)
            {
                return true;
            }

            // Refused, or no answer: nothing of this attempt stays registered. Without an answer it may have been applied, so it starts over.
            await UnsubscribeAllLockedAsync(_appToken).ConfigureAwait(false);

            if (status == WsStreamSubscribeStatus.Rejected)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Subscribes the anchor first, then each event. Called under the control lock.
    /// The transport waits on the app token, at most the response timeout; the given token is checked between requests, so every handle is kept for unsubscribe.
    /// </summary>
    private async ValueTask<WsStreamSubscribeStatus> SubscribeAllLockedAsync(byte[] key, CancellationToken cancellationToken)
    {
        // The handle is bound to its event by the transport before the request goes out, so data that follows the reply finds its parser.
        _state.ExpectAnchor();
        WsStreamSubscribeResult anchor = await _transport.SubscribeAsync(BuildName(key, "@listenKeyExpired"u8), _appToken).ConfigureAwait(false);
        _anchorHandle = anchor.Handle;

        WsStreamSubscribeStatus status = Combine(WsStreamSubscribeStatus.Subscribed, anchor.Status);

        for (int i = 0; i < _supportedEvents.Length; i++)
        {
            BinanceUserDataEvents userDataEvent = _supportedEvents[i];

            if ((_events & userDataEvent) == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            _state.ExpectEvent(userDataEvent);
            WsStreamSubscribeResult result = await _transport.SubscribeAsync(BuildName(key, SuffixOf(userDataEvent)), _appToken).ConfigureAwait(false);
            _eventHandles[i] = result.Handle;

            status = Combine(status, result.Status);
        }

        return status;
    }

    /// <summary>Unsubscribes the events, then the anchor. Called under the control lock. True when the exchange confirmed all.</summary>
    private async ValueTask<bool> UnsubscribeAllLockedAsync(CancellationToken cancellationToken)
    {
        bool confirmed = true;

        for (int i = 0; i < _eventHandles.Length; i++)
        {
            if (_eventHandles[i] < 0)
            {
                continue;
            }

            confirmed &= await _transport.UnsubscribeAsync(_eventHandles[i], cancellationToken).ConfigureAwait(false);
            _eventHandles[i] = -1;
        }

        if (0 <= _anchorHandle)
        {
            confirmed &= await _transport.UnsubscribeAsync(_anchorHandle, cancellationToken).ConfigureAwait(false);
            _anchorHandle = -1;
        }

        return confirmed;
    }

    /// <summary>Cold path. Undoes a canceled subscribe, so nothing of it stays registered. Called under the control lock.</summary>
    private async ValueTask RollBackLockedAsync()
    {
        _events = BinanceUserDataEvents.None;

        try
        {
            // The app token, not the canceled one: a canceled unsubscribe would keep its handle. On app shutdown nothing needs undoing.
            await UnsubscribeAllLockedAsync(_appToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
        catch (WsStreamFatalException)
        {
            // The transport has already told the handler.
        }
    }

    /// <summary>The account's listen key, extended for 60 minutes; null when the request failed and should be tried again.</summary>
    private async ValueTask<byte[]?> StartKeyAsync(CancellationToken cancellationToken)
    {
        WsApiRequestResult<ListenKeyModel> result = await _listenKey.StartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Outcome == RequestOutcome.Success ? result.Value.ListenKey : null;
    }

    private void OpenGap(WsDisconnectReason reason)
    {
        lock (_state.HandlerSync)
        {
            if (_state.TryOpenGap(reason))
            {
                _handler.OnDisconnected(reason, isFatal: false);
            }
        }
    }

    private void CloseGap()
    {
        lock (_state.HandlerSync)
        {
            if (_state.TryCloseGap(out WsStreamGap gap))
            {
                _handler.OnReconnected(in gap);
            }
        }
    }

    /// <summary>Cold path. The key cannot be kept without the WebSocket API: the session stops for good.</summary>
    private void EnterFatal(WsApiFatalException exception)
    {
        WsDisconnectInfo info = new() { Reason = WsDisconnectReason.ExchangeFatal, Fault = exception };
        _transport.EnterFatal(in info);
    }

    private long GetBackoffDelayMillis(int attempt) =>
        WsReconnectPolicy.GetBackoffDelayMillis(attempt, _retryBaseDelayMillis, _retryMaxDelayMillis)
        + WsReconnectPolicy.GetJitterMillis(_minJitterMillis, _maxJitterMillis);

    /// <summary>The worse of two subscribe outcomes.</summary>
    private static WsStreamSubscribeStatus Combine(WsStreamSubscribeStatus current, WsStreamSubscribeStatus next) =>
        next switch
        {
            WsStreamSubscribeStatus.Subscribed or WsStreamSubscribeStatus.AlreadySubscribed => current,
            WsStreamSubscribeStatus.Unconfirmed => current == WsStreamSubscribeStatus.Rejected ? current : next,
            _ => WsStreamSubscribeStatus.Rejected,
        };

    private static ReadOnlySpan<byte> SuffixOf(BinanceUserDataEvents userDataEvent) =>
        userDataEvent switch
        {
            BinanceUserDataEvents.OrderTradeUpdate => "@ORDER_TRADE_UPDATE"u8,
            BinanceUserDataEvents.AccountUpdate => "@ACCOUNT_UPDATE"u8,
            BinanceUserDataEvents.TradeLite => "@TRADE_LITE"u8,
            BinanceUserDataEvents.AlgoUpdate => "@ALGO_UPDATE"u8,
            BinanceUserDataEvents.AccountConfigUpdate => "@ACCOUNT_CONFIG_UPDATE"u8,
            BinanceUserDataEvents.MarginCall => "@MARGIN_CALL"u8,
            _ => ""u8,
        };

    /// <summary>Key plus event suffix, e.g. key@ORDER_TRADE_UPDATE.</summary>
    private static byte[] BuildName(byte[] key, ReadOnlySpan<byte> suffix)
    {
        byte[] name = new byte[key.Length + suffix.Length];
        key.CopyTo(name, 0);
        suffix.CopyTo(name.AsSpan(key.Length));
        return name;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowEventsNotSupported(BinanceUserDataEvents events) =>
        throw new ArgumentException("The market does not send some of these user data events.", nameof(events));
}
