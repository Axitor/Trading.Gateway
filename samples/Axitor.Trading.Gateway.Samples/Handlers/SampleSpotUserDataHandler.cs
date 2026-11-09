using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Samples.Handlers;

/// <summary>
/// Gets the spot user data of one account. Must be a readonly struct.
/// Calls run on the receive loop: return fast and never throw. In a real app, hand events to a queue.
/// Calls never overlap, and events come in the order the exchange sent them.
/// </summary>
internal readonly struct SampleSpotUserDataHandler : IBinanceSpotUserDataHandler
{
    // An order was created or changed. Average fill price is FilledQuoteQuantity / FilledQuantity.
    public void OnExecutionReport(in SpotExecutionReportModel model) =>
        Console.WriteLine($"Order {model.ClientOrderId} {model.Symbol}: {model.ExecutionType}, {model.Status}, filled {model.LastFilledQuantity} at {model.LastFilledPrice}");

    // Balances changed. The span is valid only during this call.
    public void OnAccountPosition(in SpotAccountPositionHeader header, ReadOnlySpan<SpotAccountBalance> balances)
    {
        foreach (SpotAccountBalance balance in balances)
        {
            Console.WriteLine($"{balance.Asset}: free {balance.Free}, locked {balance.Locked}");
        }
    }

    // A deposit, a withdrawal or a transfer. Delta is a change: add it once.
    public void OnBalanceUpdate(in SpotBalanceUpdateModel model) =>
        Console.WriteLine($"{model.Asset} changed by {model.Delta}");

    // An order list, e.g. an OCO, changed. The span is valid only during this call.
    public void OnListStatus(in SpotListStatusHeader header, ReadOnlySpan<SpotListStatusOrder> orders) =>
        Console.WriteLine($"List {header.ListClientOrderId} {header.ContingencyType}: {header.ListOrderStatus}, {orders.Length} orders");

    // Part of the balance was locked or unlocked by another system, e.g. as margin collateral.
    public void OnExternalLockUpdate(in SpotExternalLockUpdateModel model) =>
        Console.WriteLine($"{model.Asset} lock changed by {model.Delta}");

    public void OnDisconnected(WsDisconnectReason reason, bool isFatal) =>
        Console.WriteLine($"Spot user data stopped: {reason}, fatal {isFatal}");

    // Events inside the gap are lost: check open orders, balances and trades for that time.
    public void OnReconnected(in WsStreamGap gap) =>
        Console.WriteLine($"Spot user data is back: check orders and balances from {gap.StartUtcMillis} to {gap.EndUtcMillis}");
}
