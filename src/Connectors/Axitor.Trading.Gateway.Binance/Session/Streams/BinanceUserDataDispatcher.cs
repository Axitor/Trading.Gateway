using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.UserData;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.Streams;

/// <summary>Parses user data by the event of its subscription, drops copies and hands the event to the handler.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: User data has no growing update id, so copies from an overlapping rotation are found by their bytes.
/// A copy from the other connection is the same bytes, the event time included.
/// </para>
/// <para>
/// DESIGN NOTE: listenKeyExpired is written unlike other events: spaces after colons and E as a string.
/// It is rare, so it is read with a JSON reader on the slow path.
/// </para>
/// </remarks>
internal readonly struct BinanceUserDataDispatcher<THandler> : IWsStreamDispatcher
    where THandler : struct, IBinanceUserDataHandler
{
    private readonly THandler _handler;
    private readonly BinanceUserDataState _state;

    public BinanceUserDataDispatcher(THandler handler, BinanceUserDataState state)
    {
        _handler = handler;
        _state = state;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public WsDispatchResult Dispatch(int subscription, ReadOnlySpan<byte> data)
    {
        switch (_state.EventOf(subscription))
        {
            case BinanceUserDataEvents.OrderTradeUpdate:
                return DispatchOrderUpdate(data);

            case BinanceUserDataEvents.AccountUpdate:
                return DispatchAccountUpdate(data);

            case BinanceUserDataEvents.TradeLite:
                return DispatchTradeLite(data);

            case BinanceUserDataEvents.AlgoUpdate:
                return DispatchAlgoUpdate(data);

            case BinanceUserDataEvents.AccountConfigUpdate:
                return DispatchAccountConfigUpdate(data);

            case BinanceUserDataEvents.MarginCall:
                return DispatchMarginCall(data);
        }

        return _state.IsAnchor(subscription) ? DispatchAnchor(data) : WsDispatchResult.Unreadable;
    }

    /// <inheritdoc />
    public WsInboundSignal DispatchEvent(ReadOnlySpan<byte> message) => WsInboundSignal.None;

    /// <inheritdoc />
    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal)
    {
        // Every subscription stops for good together: told once, whichever comes first.
        if (isFatal)
        {
            lock (_state.HandlerSync)
            {
                if (_state.TryReportFatal())
                {
                    _handler.OnDisconnected(reason, isFatal: true);
                }
            }

            return;
        }

        // All subscriptions share one connection: the anchor speaks for them.
        if (!_state.IsAnchor(subscription))
        {
            return;
        }

        lock (_state.HandlerSync)
        {
            if (_state.TryOpenGap(reason))
            {
                _handler.OnDisconnected(reason, isFatal: false);
            }
        }
    }

    /// <inheritdoc />
    public void OnReconnected(int subscription, in WsStreamGap gap)
    {
        // A key that died while offline was resubscribed silently, so the gap closes only after the key is checked.
        if (_state.IsAnchor(subscription))
        {
            _state.SignalReconnected();
        }
    }

    /// <inheritdoc />
    public void OnSubscribing(int subscription) => _state.OnSubscribing(subscription);

    /// <inheritdoc />
    public void OnUnsubscribed(int subscription)
    {
        // The session removes its own subscriptions while it moves keys or stops; the handler works with events, not handles.
    }

    private WsDispatchResult DispatchOrderUpdate(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = OrderUpdateParser.Read(data, out OrderUpdateModel model);

        if (result == WsDispatchResult.Fatal)
        {
            return result;
        }

        // A copy was already handled and reported.
        if (_state.Duplicates.IsDuplicate(model.EventTime, data))
        {
            return WsDispatchResult.Handled;
        }

        lock (_state.HandlerSync)
        {
            _handler.OnOrderUpdate(in model);
        }

        return result;
    }

    private WsDispatchResult DispatchAccountUpdate(ReadOnlySpan<byte> data)
    {
        AccountUpdateBalance[] balances = _state.Balances;
        AccountUpdatePosition[] positions = _state.Positions;

        WsDispatchResult result = AccountUpdateParser.Read(data, out AccountUpdateHeader header, ref balances, out int balanceCount, ref positions, out int positionCount);

        // A grown buffer is kept for the next update.
        _state.Balances = balances;
        _state.Positions = positions;

        if (result == WsDispatchResult.Fatal)
        {
            return result;
        }

        if (_state.Duplicates.IsDuplicate(header.EventTime, data))
        {
            return WsDispatchResult.Handled;
        }

        lock (_state.HandlerSync)
        {
            _handler.OnAccountUpdate(in header, balances.AsSpan(0, balanceCount), positions.AsSpan(0, positionCount));
        }

        return result;
    }

    private WsDispatchResult DispatchTradeLite(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = TradeLiteParser.Read(data, out TradeLiteModel model);

        if (result == WsDispatchResult.Fatal)
        {
            return result;
        }

        if (_state.Duplicates.IsDuplicate(model.EventTime, data))
        {
            return WsDispatchResult.Handled;
        }

        lock (_state.HandlerSync)
        {
            _handler.OnTradeLite(in model);
        }

        return result;
    }

    private WsDispatchResult DispatchAlgoUpdate(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = AlgoUpdateParser.Read(data, out AlgoUpdateModel model);

        if (result == WsDispatchResult.Fatal)
        {
            return result;
        }

        if (_state.Duplicates.IsDuplicate(model.EventTime, data))
        {
            return WsDispatchResult.Handled;
        }

        lock (_state.HandlerSync)
        {
            _handler.OnAlgoUpdate(in model);
        }

        return result;
    }

    private WsDispatchResult DispatchAccountConfigUpdate(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = AccountConfigUpdateParser.Read(data, out AccountConfigUpdateModel model);

        if (result == WsDispatchResult.Fatal)
        {
            return result;
        }

        if (_state.Duplicates.IsDuplicate(model.EventTime, data))
        {
            return WsDispatchResult.Handled;
        }

        lock (_state.HandlerSync)
        {
            _handler.OnAccountConfigUpdate(in model);
        }

        return result;
    }

    private WsDispatchResult DispatchMarginCall(ReadOnlySpan<byte> data)
    {
        MarginCallPosition[] positions = _state.MarginCallPositions;

        WsDispatchResult result = MarginCallParser.Read(data, out MarginCallHeader header, ref positions, out int positionCount);

        // A grown buffer is kept for the next call.
        _state.MarginCallPositions = positions;

        if (result == WsDispatchResult.Fatal)
        {
            return result;
        }

        if (_state.Duplicates.IsDuplicate(header.EventTime, data))
        {
            return WsDispatchResult.Handled;
        }

        lock (_state.HandlerSync)
        {
            _handler.OnMarginCall(in header, positions.AsSpan(0, positionCount));
        }

        return result;
    }

    /// <summary>Cold path. Anything on the anchor may be an expiry, so the key loop is woken unless the event names an older key.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WsDispatchResult DispatchAnchor(ReadOnlySpan<byte> data)
    {
        bool isExpiry = false;
        bool isOtherKey = false;

        try
        {
            Utf8JsonReader reader = new(data);

            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("e"u8))
                    {
                        reader.Read();
                        isExpiry = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("listenKeyExpired"u8);
                    }
                    else if (reader.ValueTextEquals("listenKey"u8))
                    {
                        reader.Read();
                        isOtherKey = reader.TokenType == JsonTokenType.String && !reader.ValueTextEquals(_state.ListenKey);
                    }
                    else
                    {
                        reader.Read();
                    }

                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            // Unreadable: treated as an expiry below.
        }

        // An expiry of a key already replaced changes nothing.
        if (isExpiry && isOtherKey)
        {
            return WsDispatchResult.Handled;
        }

        // A false alarm costs one key check; a missed expiry leaves the session blind.
        _state.SignalExpired();
        return isExpiry ? WsDispatchResult.Handled : WsDispatchResult.NonStandard;
    }
}
