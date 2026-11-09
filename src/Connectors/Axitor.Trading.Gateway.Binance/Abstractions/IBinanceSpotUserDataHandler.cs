using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Receives the spot user data of one account. Implement as a readonly struct; keep state in a class it holds.</summary>
/// <remarks>
/// <para>Calls run on the receive loop: return fast and never throw. A thrown exception stops the session for good. Hand heavy work to a channel.</para>
/// <para>Calls never overlap, and events come in the order the exchange sent them, e.g. a fill before the balance change it caused.</para>
/// <para>A value the library does not know reads as UNKNOWN, or Empty for a client order id; the event still comes and telemetry gets OnWsApiMessageNonStandard.</para>
/// </remarks>
public interface IBinanceSpotUserDataHandler
{
    /// <summary>An order was created or changed.</summary>
    void OnExecutionReport(in SpotExecutionReportModel model);

    /// <summary>Balances changed. The span is valid only during the call.</summary>
    void OnAccountPosition(in SpotAccountPositionHeader header, ReadOnlySpan<SpotAccountBalance> balances);

    /// <summary>A deposit, a withdrawal or a transfer between wallets.</summary>
    void OnBalanceUpdate(in SpotBalanceUpdateModel model);

    /// <summary>An order list changed. The span is valid only during the call.</summary>
    void OnListStatus(in SpotListStatusHeader header, ReadOnlySpan<SpotListStatusOrder> orders);

    /// <summary>Part of the balance was locked or unlocked by another system, e.g. as margin collateral.</summary>
    void OnExternalLockUpdate(in SpotExternalLockUpdateModel model);

    /// <summary>Events stopped. A fatal stop never resumes; the session Completion faults.</summary>
    void OnDisconnected(WsDisconnectReason reason, bool isFatal);

    /// <summary>Events flow again, but events inside the gap are lost: check open orders, balances and trades for the gap window. Can come just after the first new event.</summary>
    void OnReconnected(in WsStreamGap gap);
}
