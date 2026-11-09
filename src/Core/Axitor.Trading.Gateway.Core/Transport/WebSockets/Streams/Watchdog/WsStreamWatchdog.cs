using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Configuration;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Resiliency;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Wire;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Watchdog;

/// <summary>Listens to a stream that never goes quiet; when it does, checks the network and reconnects.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Some failures leave a connection alive, with pongs coming back, while no data arrives.
/// Ping and pong cannot see this, so the watchdog listens to a stream that is never quiet in normal work.
/// Silence counts only while the connection is up and has already delivered.
/// </para>
/// <para>
/// DESIGN NOTE: When the watchdog barks, its own connection and every user stream connection are dropped.
/// Their data is stuck anyway: each subscription gets a gap and is subscribed again on reconnect.
/// </para>
/// <para>
/// DESIGN NOTE: The WebSocket API is not dropped on a bark. Dropping it would give orders in flight an unknown status,
/// and it may be healthy: the silence can come from one stuck stream node.
/// </para>
/// <para>
/// DESIGN NOTE: A bark trips the network gate. Until a REST ping gets through, REST, new WebSocket API requests
/// and every reconnect wait for the repair.
/// </para>
/// <para>
/// DESIGN NOTE: After every network repair, whoever tripped the gate, each WebSocket API lane and each stream connection
/// is replaced make-before-break: the new connection takes new requests at once, the old one drains and closes.
/// A connection may have died quietly during the outage, for example after an address change, and ping and pong
/// need a few seconds to notice. Nothing new goes into it, and requests already on it still get their replies if it lived.
/// </para>
/// <para>
/// DESIGN NOTE: A user stream is never dropped for its own silence: a quiet alt stream is normal.
/// </para>
/// </remarks>
internal sealed class WsStreamWatchdog<TDialect, TLimiter, TTelemetry> : IDisposable
    where TDialect : struct, IExchangeDialectWsStream
    where TLimiter : struct, IRateLimiter
    where TTelemetry : struct, ITelemetryHandler
{
    private readonly WsStreamTransport<TDialect, TLimiter, WsStreamWatchdogDispatcher, TTelemetry> _transport;
    private readonly StrongBox<long> _lastTicks = new();
    private readonly PeriodicTimer _timer;
    private readonly TTelemetry _telemetry;
    private readonly byte[] _stream;
    private readonly long _timeoutTicks;
    private readonly TimeSpan _timeout;
    private readonly Action<WsDisconnectReason> _abortStreams;

    /// <summary>Creates a watchdog on its own single connection. Nothing connects until Start.</summary>
    public WsStreamWatchdog(
        WsStreamTransportOptions options,
        Uri uri,
        byte[] stream,
        int messagesPerSecond,
        int connectLimitBucket,
        int connectWeight,
        TLimiter limiter,
        TTelemetry telemetry,
        BanGate banGate,
        NetworkGate networkGate,
        NetworkRepairWorker networkRepairWorker,
        Action<WsDisconnectReason> abortStreams)
    {
        _telemetry = telemetry;
        _stream = stream;
        _timeout = options.Resiliency.WatchdogTimeout;
        _timeoutTicks = SysTime.TicksFromMillis((long)_timeout.TotalMilliseconds);
        _timer = new PeriodicTimer(_timeout / 2);
        _abortStreams = abortStreams;

        // One stream on one connection; the user's shard settings do not apply.
        WsStreamTransportOptions single = options with
        {
            Network = options.Network with { MaxConnections = 1, MaxStreamsPerConnection = 1 },
        };

        _transport = new(single, uri, messagesPerSecond, connectLimitBucket, connectWeight,
            limiter, new WsStreamWatchdogDispatcher(_lastTicks), telemetry, banGate, networkGate, networkRepairWorker, isWatchdog: true);
    }

    /// <summary>Connects, subscribes and starts watching. Call once.</summary>
    public void Start(CancellationToken appCancellationToken)
    {
        _transport.Start(appCancellationToken);
        _ = SubscribeAsync(appCancellationToken);
        _ = MonitorAsync(appCancellationToken);
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                WsStreamSubscribeResult result = await _transport.SubscribeAsync(_stream, cancellationToken).ConfigureAwait(false);

                // Subscribed or unconfirmed: registered either way, and a reconnect keeps it.
                if (result.Status != WsStreamSubscribeStatus.Rejected)
                {
                    return;
                }

                await Task.Delay(_timeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
        catch (WsStreamFatalException)
        {
            // Already reported: OnWatchdogConnectFailed for a refused handshake, the ban gate for a long ban.
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                long lastTicks = Volatile.Read(ref _lastTicks.Value);

                // Not armed: connecting, reconnecting, or no data yet.
                if (lastTicks == 0)
                {
                    continue;
                }

                long silentTicks = SysTime.GetTimestamp() - lastTicks;

                if (silentTicks < _timeoutTicks)
                {
                    continue;
                }

                // Disarm only if no message came meanwhile: one silence barks once.
                if (Interlocked.CompareExchange(ref _lastTicks.Value, 0, lastTicks) != lastTicks)
                {
                    continue;
                }

                _telemetry.OnWatchdogSilent(SysTime.MillisFromTicks(silentTicks));
                _transport.AbortAll(WsDisconnectReason.WatchdogTimeout);

                // User connections may be stuck the same way: they reconnect too, and every subscription gets a gap.
                _abortStreams(WsDisconnectReason.WatchdogTimeout);
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown.
        }
    }

    /// <summary>Stops watching and releases the connection.</summary>
    public void Dispose()
    {
        _timer.Dispose();
        _transport.Dispose();
    }
}
