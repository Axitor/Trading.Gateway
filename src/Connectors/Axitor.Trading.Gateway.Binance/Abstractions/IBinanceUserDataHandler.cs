using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Receives the user data of one account. Implement as a readonly struct; keep state in a class it holds.</summary>
/// <remarks>
/// <para>Calls run on the receive loop: return fast and never throw. A thrown exception stops the session for good. Hand heavy work to a channel.</para>
/// <para>Calls never overlap, and events come in the order the exchange sent them, e.g. a fill before the position change it caused.</para>
/// <para>A value the library does not know reads as UNKNOWN, or Empty for a client order id; the event still comes and telemetry gets OnWsStreamMessageNonStandard.</para>
/// </remarks>
public interface IBinanceUserDataHandler
{
    /// <summary>An order was created or changed.</summary>
    void OnOrderUpdate(in OrderUpdateModel model);

    /// <summary>Balances or positions changed. The spans are valid only during the call.</summary>
    void OnAccountUpdate(in AccountUpdateHeader header, ReadOnlySpan<AccountUpdateBalance> balances, ReadOnlySpan<AccountUpdatePosition> positions);

    /// <summary>An order was filled, reported before its OnOrderUpdate. Count fills from one of the two, or each fill is counted twice. USD-M only.</summary>
    void OnTradeLite(in TradeLiteModel model);

    /// <summary>An algo order, e.g. a stop loss or take profit, was created or changed. USD-M only.</summary>
    void OnAlgoUpdate(in AlgoUpdateModel model);

    /// <summary>The leverage of a symbol or, on USD-M, Multi-Assets mode changed.</summary>
    void OnAccountConfigUpdate(in AccountConfigUpdateModel model);

    /// <summary>Position risk is too high. A warning only: the position may already be liquidated. The span is valid only during the call.</summary>
    void OnMarginCall(in MarginCallHeader header, ReadOnlySpan<MarginCallPosition> positions);

    /// <summary>Events stopped. A fatal stop never resumes; the session Completion faults.</summary>
    void OnDisconnected(WsDisconnectReason reason, bool isFatal);

    /// <summary>Events flow again, but events inside the gap are lost: check open orders, positions, balances and trades for the gap window. Can come just after the first new event.</summary>
    void OnReconnected(in WsStreamGap gap);
}
