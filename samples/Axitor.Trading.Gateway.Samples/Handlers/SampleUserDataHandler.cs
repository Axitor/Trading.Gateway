using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

namespace Axitor.Trading.Gateway.Samples.Handlers;

/// <summary>
/// Gets the futures user data of one account. Must be a readonly struct.
/// Calls run on the receive loop: return fast and never throw. In a real app, hand events to a queue.
/// Calls never overlap, and events come in the order the exchange sent them.
/// </summary>
internal readonly struct SampleUserDataHandler : IBinanceUserDataHandler
{
    // An order was created or changed: placed, filled, canceled, expired.
    public void OnOrderUpdate(in OrderUpdateModel model) =>
        Console.WriteLine($"Order {model.ClientOrderId} {model.Symbol}: {model.ExecutionType}, {model.Status}, filled {model.LastFilledQuantity} at {model.LastFilledPrice}");

    // Balances or positions changed. The spans are valid only during this call.
    public void OnAccountUpdate(in AccountUpdateHeader header, ReadOnlySpan<AccountUpdateBalance> balances, ReadOnlySpan<AccountUpdatePosition> positions)
    {
        Console.WriteLine($"Account update: {header.Reason}");

        foreach (AccountUpdateBalance balance in balances)
        {
            Console.WriteLine($"  {balance.Asset}: wallet {balance.WalletBalance}");
        }

        foreach (AccountUpdatePosition position in positions)
        {
            Console.WriteLine($"  {position.Symbol} {position.Side}: {position.Quantity} at {position.EntryPrice}");
        }
    }

    // A fill, sent before its OnOrderUpdate. Count fills from one of the two only.
    public void OnTradeLite(in TradeLiteModel model) =>
        Console.WriteLine($"Fill {model.ClientOrderId}: {model.LastFilledQuantity} at {model.LastFilledPrice}");

    // A stop loss, take profit or trailing stop was created or changed.
    public void OnAlgoUpdate(in AlgoUpdateModel model) =>
        Console.WriteLine($"Algo order {model.ClientAlgoId}: {model.Type}, {model.Status}");

    public void OnAccountConfigUpdate(in AccountConfigUpdateModel model) =>
        Console.WriteLine(model.IsMultiAssetsModeChange ? $"Multi-Assets mode: {model.IsMultiAssetsMode}" : $"{model.Symbol}: leverage {model.Leverage}");

    // Position risk is too high. A warning only: the position may already be liquidated.
    public void OnMarginCall(in MarginCallHeader header, ReadOnlySpan<MarginCallPosition> positions)
    {
        foreach (MarginCallPosition position in positions)
        {
            Console.WriteLine($"Margin call {position.Symbol}: mark {position.MarkPrice}, maint margin {position.MaintenanceMargin}");
        }
    }

    public void OnDisconnected(WsDisconnectReason reason, bool isFatal) =>
        Console.WriteLine($"User data stopped: {reason}, fatal {isFatal}");

    // Events inside the gap are lost: check open orders, positions, balances and trades for that time.
    public void OnReconnected(in WsStreamGap gap) =>
        Console.WriteLine($"User data is back: check orders and positions from {gap.StartUtcMillis} to {gap.EndUtcMillis}");
}
