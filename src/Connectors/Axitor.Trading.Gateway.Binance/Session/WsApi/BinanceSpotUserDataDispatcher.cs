using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.UserData;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Session.WsApi;

/// <summary>Cuts the event out of its subscription envelope, routes it by type, drops copies and hands it to the handler.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Copies from an overlapping rotation are found by the event bytes only: the envelope carries a subscription id that differs between connections.
/// </para>
/// <para>
/// DESIGN NOTE: eventStreamTerminated of a signature subscription comes only after our unsubscribe. Any other one is unexpected:
/// it counts only from the connection holding our subscription and outside an unsubscribe, opens a gap and asks the session to subscribe again.
/// </para>
/// <para>
/// DESIGN NOTE: The dedicated connection gets serverShutdown too; it starts a rotation like on the shared connections.
/// </para>
/// </remarks>
internal readonly struct BinanceSpotUserDataDispatcher<THandler> : IWsApiEventDispatcher
    where THandler : struct, IBinanceSpotUserDataHandler
{
    private readonly THandler _handler;
    private readonly BinanceSpotUserDataState _state;

    public BinanceSpotUserDataDispatcher(THandler handler, BinanceSpotUserDataState state)
    {
        _handler = handler;
        _state = state;
    }

    /// <inheritdoc />
    public WsDispatchResult Dispatch(ReadOnlySpan<byte> message, int connectionEpoch, out WsInboundSignal signal)
    {
        signal = WsInboundSignal.None;

        if (!TryReadEnvelope(message, out int eventStart, out int eventLength, out EventKind kind))
        {
            return WsDispatchResult.Unreadable;
        }

        ReadOnlySpan<byte> data = message.Slice(eventStart, eventLength);

        switch (kind)
        {
            case EventKind.ExecutionReport:
                return DispatchExecutionReport(data);

            case EventKind.AccountPosition:
                return DispatchAccountPosition(data);

            case EventKind.BalanceUpdate:
                return DispatchBalanceUpdate(data);

            case EventKind.ListStatus:
                return DispatchListStatus(data);

            case EventKind.ExternalLockUpdate:
                return DispatchExternalLockUpdate(data);

            case EventKind.StreamTerminated:
                return OnStreamTerminated(connectionEpoch);

            case EventKind.ServerShutdown:
                signal = WsInboundSignal.ServerShutdown;
                return WsDispatchResult.Handled;
        }

        return WsDispatchResult.Unreadable;
    }

    /// <inheritdoc />
    public void OnDisconnected(WsDisconnectReason reason, bool isFatal)
    {
        lock (_state.HandlerSync)
        {
            // Told once, whether the user is subscribed or not.
            if (isFatal)
            {
                if (_state.TryReportFatal())
                {
                    _handler.OnDisconnected(reason, isFatal: true);
                }

                return;
            }

            if (_state.IsWanted && _state.TryOpenGap(reason))
            {
                _handler.OnDisconnected(reason, isFatal: false);
            }
        }
    }

    /// <inheritdoc />
    public void OnReconnected(in WsStreamGap gap)
    {
        // Usually the hook has closed the gap already; this one is left for a drop while the hook ran.
        lock (_state.HandlerSync)
        {
            if (_state.IsWanted && _state.TryCloseGap(gap.StartUtcMillis, out WsStreamGap closed))
            {
                _handler.OnReconnected(in closed);
            }
        }
    }

    private WsDispatchResult DispatchExecutionReport(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = SpotExecutionReportParser.Read(data, out SpotExecutionReportModel model);

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
            if (_state.IsWanted)
            {
                _handler.OnExecutionReport(in model);
            }
        }

        return result;
    }

    private WsDispatchResult DispatchAccountPosition(ReadOnlySpan<byte> data)
    {
        SpotAccountBalance[] balances = _state.Balances;

        WsDispatchResult result = SpotAccountPositionParser.Read(data, out SpotAccountPositionHeader header, ref balances, out int balanceCount);

        // A grown buffer is kept for the next event.
        _state.Balances = balances;

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
            if (_state.IsWanted)
            {
                _handler.OnAccountPosition(in header, balances.AsSpan(0, balanceCount));
            }
        }

        return result;
    }

    private WsDispatchResult DispatchBalanceUpdate(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = SpotBalanceUpdateParser.Read(data, out SpotBalanceUpdateModel model);

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
            if (_state.IsWanted)
            {
                _handler.OnBalanceUpdate(in model);
            }
        }

        return result;
    }

    private WsDispatchResult DispatchListStatus(ReadOnlySpan<byte> data)
    {
        SpotListStatusOrder[] orders = _state.ListOrders;

        WsDispatchResult result = SpotListStatusParser.Read(data, out SpotListStatusHeader header, ref orders, out int orderCount);

        // A grown buffer is kept for the next event.
        _state.ListOrders = orders;

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
            if (_state.IsWanted)
            {
                _handler.OnListStatus(in header, orders.AsSpan(0, orderCount));
            }
        }

        return result;
    }

    private WsDispatchResult DispatchExternalLockUpdate(ReadOnlySpan<byte> data)
    {
        WsDispatchResult result = SpotExternalLockUpdateParser.Read(data, out SpotExternalLockUpdateModel model);

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
            if (_state.IsWanted)
            {
                _handler.OnExternalLockUpdate(in model);
            }
        }

        return result;
    }

    /// <summary>Cold path. The subscription ended without our unsubscribe: a gap until the session subscribes again.</summary>
    private WsDispatchResult OnStreamTerminated(int connectionEpoch)
    {
        lock (_state.HandlerSync)
        {
            // Our own unsubscribe, or a connection that no longer holds our subscription.
            if (!_state.IsWanted || !_state.TryEndSubscription(connectionEpoch))
            {
                return WsDispatchResult.Handled;
            }

            if (_state.TryOpenGap(WsDisconnectReason.SubscriptionExpired))
            {
                _handler.OnDisconnected(WsDisconnectReason.SubscriptionExpired, isFatal: false);
            }
        }

        _state.RequestResubscribe();
        return WsDispatchResult.Handled;
    }

    /// <summary>Finds the event object in the envelope and its type. False when the message has no event object.</summary>
    private static bool TryReadEnvelope(ReadOnlySpan<byte> message, out int eventStart, out int eventLength, out EventKind kind)
    {
        eventStart = 0;
        eventLength = 0;
        kind = EventKind.Unknown;
        bool found = false;

        try
        {
            Utf8JsonReader reader = new(message);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (!reader.ValueTextEquals("event"u8))
                {
                    reader.Read();
                    reader.Skip();
                    continue;
                }

                reader.Read();

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    return false;
                }

                eventStart = (int)reader.TokenStartIndex;

                // A copy reads ahead to the type; the original skips the whole object.
                Utf8JsonReader probe = reader;
                kind = ReadEventKind(ref probe);

                reader.Skip();
                eventLength = (int)reader.BytesConsumed - eventStart;
                found = true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return found;
    }

    /// <summary>Reads the event object the reader is on until its type.</summary>
    private static EventKind ReadEventKind(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("e"u8))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.String ? ToEventKind(ref reader) : EventKind.Unknown;
            }

            reader.Read();
            reader.Skip();
        }

        return EventKind.Unknown;
    }

    private static EventKind ToEventKind(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("executionReport"u8)) return EventKind.ExecutionReport;
        if (reader.ValueTextEquals("outboundAccountPosition"u8)) return EventKind.AccountPosition;
        if (reader.ValueTextEquals("balanceUpdate"u8)) return EventKind.BalanceUpdate;
        if (reader.ValueTextEquals("listStatus"u8)) return EventKind.ListStatus;
        if (reader.ValueTextEquals("externalLockUpdate"u8)) return EventKind.ExternalLockUpdate;
        if (reader.ValueTextEquals("eventStreamTerminated"u8)) return EventKind.StreamTerminated;
        if (reader.ValueTextEquals("serverShutdown"u8)) return EventKind.ServerShutdown;

        return EventKind.Unknown;
    }

    /// <summary>Event types the dispatcher routes.</summary>
    private enum EventKind : byte
    {
        Unknown,
        ExecutionReport,
        AccountPosition,
        BalanceUpdate,
        ListStatus,
        ExternalLockUpdate,
        StreamTerminated,
        ServerShutdown,
    }
}
