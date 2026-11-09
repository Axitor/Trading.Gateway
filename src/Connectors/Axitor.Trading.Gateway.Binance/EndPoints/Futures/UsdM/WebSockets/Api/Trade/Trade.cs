using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.CancelAlgoOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.CancelOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.ModifyOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewAlgoOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewOrder;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade;

/// <summary>Binance Futures UsdM WebSocket API Trade endpoints.</summary>
public sealed class Trade<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>New Order endpoint.</summary>
    public NewOrderEndpoint<TTelemetry> NewOrder { get; init; }

    /// <summary>Cancel Order endpoint.</summary>
    public CancelOrderEndpoint<TTelemetry> CancelOrder { get; init; }

    /// <summary>Modify Order endpoint.</summary>
    public ModifyOrderEndpoint<TTelemetry> ModifyOrder { get; init; }

    /// <summary>New Algo Order endpoint.</summary>
    public NewAlgoOrderEndpoint<TTelemetry> NewAlgoOrder { get; init; }

    /// <summary>Cancel Algo Order endpoint.</summary>
    public CancelAlgoOrderEndpoint<TTelemetry> CancelAlgoOrder { get; init; }

    internal Trade(BinanceRouter<TTelemetry> router)
    {
        NewOrder = new NewOrderEndpoint<TTelemetry>(router);
        CancelOrder = new CancelOrderEndpoint<TTelemetry>(router);
        ModifyOrder = new ModifyOrderEndpoint<TTelemetry>(router);
        NewAlgoOrder = new NewAlgoOrderEndpoint<TTelemetry>(router);
        CancelAlgoOrder = new CancelAlgoOrderEndpoint<TTelemetry>(router);
    }
}
