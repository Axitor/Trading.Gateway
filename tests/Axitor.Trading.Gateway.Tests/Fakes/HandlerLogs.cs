using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Tests.Fakes;

/// <summary>A connection event a handler was told about.</summary>
internal enum HandlerEventKind : byte
{
    Disconnected,
    Reconnected,
    Unsubscribed,
}

/// <summary>One connection event. Subscription is -1 for user data, which has none.</summary>
internal readonly record struct HandlerEvent(HandlerEventKind Kind, int Subscription, WsDisconnectReason Reason, bool IsFatal, WsStreamGap Gap);

/// <summary>One message of a stream subscription.</summary>
internal readonly record struct StreamRecord<TModel>(int Subscription, TModel Model) where TModel : struct;

/// <summary>One depth update with copies of its levels: the spans the handler gets are valid only during the call.</summary>
internal readonly record struct DepthRecord(int Subscription, DepthUpdateHeader Header, DepthLevel[] Bids, DepthLevel[] Asks);

/// <summary>What a stream handler was told, in order. Safe to read while a receive loop writes.</summary>
internal sealed class StreamLog<TModel> where TModel : struct
{
    private readonly Lock _sync = new();
    private readonly List<StreamRecord<TModel>> _data = [];
    private readonly List<HandlerEvent> _events = [];

    public StreamLogHandler<TModel> Handler => new(this);

    public IReadOnlyList<StreamRecord<TModel>> Data
    {
        get
        {
            lock (_sync)
            {
                return [.. _data];
            }
        }
    }

    public IReadOnlyList<HandlerEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    internal void AddData(int subscription, in TModel model)
    {
        lock (_sync)
        {
            _data.Add(new StreamRecord<TModel>(subscription, model));
        }
    }

    internal void AddEvent(in HandlerEvent value)
    {
        lock (_sync)
        {
            _events.Add(value);
        }
    }
}

internal readonly struct StreamLogHandler<TModel> : IBinanceStreamHandler<TModel> where TModel : struct
{
    private readonly StreamLog<TModel> _log;

    public StreamLogHandler(StreamLog<TModel> log) => _log = log;

    public void OnData(int subscription, in TModel model) => _log.AddData(subscription, in model);

    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Disconnected, subscription, reason, isFatal, default));

    public void OnReconnected(int subscription, in WsStreamGap gap) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Reconnected, subscription, WsDisconnectReason.None, false, gap));

    public void OnUnsubscribed(int subscription) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Unsubscribed, subscription, WsDisconnectReason.None, false, default));
}

/// <summary>What a depth handler was told, in order. Safe to read while a receive loop writes.</summary>
internal sealed class DepthLog
{
    private readonly Lock _sync = new();
    private readonly List<DepthRecord> _data = [];
    private readonly List<HandlerEvent> _events = [];

    public DepthLogHandler Handler => new(this);

    public IReadOnlyList<DepthRecord> Data
    {
        get
        {
            lock (_sync)
            {
                return [.. _data];
            }
        }
    }

    public IReadOnlyList<HandlerEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    internal void AddData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks)
    {
        lock (_sync)
        {
            _data.Add(new DepthRecord(subscription, header, bids.ToArray(), asks.ToArray()));
        }
    }

    internal void AddEvent(in HandlerEvent value)
    {
        lock (_sync)
        {
            _events.Add(value);
        }
    }
}

internal readonly struct DepthLogHandler : IBinanceDepthHandler
{
    private readonly DepthLog _log;

    public DepthLogHandler(DepthLog log) => _log = log;

    public void OnData(int subscription, in DepthUpdateHeader header, ReadOnlySpan<DepthLevel> bids, ReadOnlySpan<DepthLevel> asks) =>
        _log.AddData(subscription, in header, bids, asks);

    public void OnDisconnected(int subscription, WsDisconnectReason reason, bool isFatal) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Disconnected, subscription, reason, isFatal, default));

    public void OnReconnected(int subscription, in WsStreamGap gap) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Reconnected, subscription, WsDisconnectReason.None, false, gap));

    public void OnUnsubscribed(int subscription) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Unsubscribed, subscription, WsDisconnectReason.None, false, default));
}

/// <summary>What a futures user data handler was told. Sequence holds event names in arrival order.</summary>
internal sealed class UserDataLog
{
    private readonly Lock _sync = new();
    private readonly List<string> _sequence = [];
    private readonly List<OrderUpdateModel> _orderUpdates = [];
    private readonly List<HandlerEvent> _events = [];

    public UserDataLogHandler Handler => new(this);

    public IReadOnlyList<string> Sequence
    {
        get
        {
            lock (_sync)
            {
                return [.. _sequence];
            }
        }
    }

    public IReadOnlyList<OrderUpdateModel> OrderUpdates
    {
        get
        {
            lock (_sync)
            {
                return [.. _orderUpdates];
            }
        }
    }

    public IReadOnlyList<HandlerEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    internal void AddOrderUpdate(in OrderUpdateModel model)
    {
        lock (_sync)
        {
            _sequence.Add(nameof(IBinanceUserDataHandler.OnOrderUpdate));
            _orderUpdates.Add(model);
        }
    }

    internal void AddName(string name)
    {
        lock (_sync)
        {
            _sequence.Add(name);
        }
    }

    internal void AddEvent(in HandlerEvent value)
    {
        lock (_sync)
        {
            _events.Add(value);
        }
    }
}

internal readonly struct UserDataLogHandler : IBinanceUserDataHandler
{
    private readonly UserDataLog _log;

    public UserDataLogHandler(UserDataLog log) => _log = log;

    public void OnOrderUpdate(in OrderUpdateModel model) => _log.AddOrderUpdate(in model);

    public void OnAccountUpdate(in AccountUpdateHeader header, ReadOnlySpan<AccountUpdateBalance> balances, ReadOnlySpan<AccountUpdatePosition> positions) =>
        _log.AddName(nameof(OnAccountUpdate));

    public void OnTradeLite(in TradeLiteModel model) => _log.AddName(nameof(OnTradeLite));

    public void OnAlgoUpdate(in AlgoUpdateModel model) => _log.AddName(nameof(OnAlgoUpdate));

    public void OnAccountConfigUpdate(in AccountConfigUpdateModel model) => _log.AddName(nameof(OnAccountConfigUpdate));

    public void OnMarginCall(in MarginCallHeader header, ReadOnlySpan<MarginCallPosition> positions) => _log.AddName(nameof(OnMarginCall));

    public void OnDisconnected(WsDisconnectReason reason, bool isFatal) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Disconnected, -1, reason, isFatal, default));

    public void OnReconnected(in WsStreamGap gap) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Reconnected, -1, WsDisconnectReason.None, false, gap));
}

/// <summary>What a spot user data handler was told. Sequence holds event names in arrival order.</summary>
internal sealed class SpotUserDataLog
{
    private readonly Lock _sync = new();
    private readonly List<string> _sequence = [];
    private readonly List<SpotExecutionReportModel> _executionReports = [];
    private readonly List<HandlerEvent> _events = [];

    public SpotUserDataLogHandler Handler => new(this);

    public IReadOnlyList<string> Sequence
    {
        get
        {
            lock (_sync)
            {
                return [.. _sequence];
            }
        }
    }

    public IReadOnlyList<SpotExecutionReportModel> ExecutionReports
    {
        get
        {
            lock (_sync)
            {
                return [.. _executionReports];
            }
        }
    }

    public IReadOnlyList<HandlerEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    internal void AddExecutionReport(in SpotExecutionReportModel model)
    {
        lock (_sync)
        {
            _sequence.Add(nameof(IBinanceSpotUserDataHandler.OnExecutionReport));
            _executionReports.Add(model);
        }
    }

    internal void AddName(string name)
    {
        lock (_sync)
        {
            _sequence.Add(name);
        }
    }

    internal void AddEvent(in HandlerEvent value)
    {
        lock (_sync)
        {
            _events.Add(value);
        }
    }
}

internal readonly struct SpotUserDataLogHandler : IBinanceSpotUserDataHandler
{
    private readonly SpotUserDataLog _log;

    public SpotUserDataLogHandler(SpotUserDataLog log) => _log = log;

    public void OnExecutionReport(in SpotExecutionReportModel model) => _log.AddExecutionReport(in model);

    public void OnAccountPosition(in SpotAccountPositionHeader header, ReadOnlySpan<SpotAccountBalance> balances) => _log.AddName(nameof(OnAccountPosition));

    public void OnBalanceUpdate(in SpotBalanceUpdateModel model) => _log.AddName(nameof(OnBalanceUpdate));

    public void OnListStatus(in SpotListStatusHeader header, ReadOnlySpan<SpotListStatusOrder> orders) => _log.AddName(nameof(OnListStatus));

    public void OnExternalLockUpdate(in SpotExternalLockUpdateModel model) => _log.AddName(nameof(OnExternalLockUpdate));

    public void OnDisconnected(WsDisconnectReason reason, bool isFatal) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Disconnected, -1, reason, isFatal, default));

    public void OnReconnected(in WsStreamGap gap) =>
        _log.AddEvent(new HandlerEvent(HandlerEventKind.Reconnected, -1, WsDisconnectReason.None, false, gap));
}
