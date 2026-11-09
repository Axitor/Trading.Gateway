using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelAlgoOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelAllAlgoOpenOrders;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelAllOpenOrders;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeLeverage;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeMarginType;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ModifyOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewAlgoOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.OpenAlgoOrders;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.OpenOrders;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.TestOrder;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade;

/// <summary>Binance Futures UsdM REST API Trade endpoints.</summary>
public sealed class Trade<TTelemetry> where TTelemetry : struct, ITelemetryHandler
{
    /// <summary>New Order endpoint.</summary>
    public NewOrderEndpoint<TTelemetry> NewOrder { get; init; }

    /// <summary>Cancel Order endpoint.</summary>
    public CancelOrderEndpoint<TTelemetry> CancelOrder { get; init; }

    /// <summary>Modify Order endpoint.</summary>
    public ModifyOrderEndpoint<TTelemetry> ModifyOrder { get; init; }

    /// <summary>Test Order endpoint.</summary>
    public TestOrderEndpoint<TTelemetry> TestOrder { get; init; }

    /// <summary>New Algo Order endpoint.</summary>
    public NewAlgoOrderEndpoint<TTelemetry> NewAlgoOrder { get; init; }

    /// <summary>Cancel Algo Order endpoint.</summary>
    public CancelAlgoOrderEndpoint<TTelemetry> CancelAlgoOrder { get; init; }

    /// <summary>Current All Open Orders endpoint.</summary>
    public OpenOrdersEndpoint<TTelemetry> OpenOrders { get; init; }

    /// <summary>Current All Algo Open Orders endpoint.</summary>
    public OpenAlgoOrdersEndpoint<TTelemetry> OpenAlgoOrders { get; init; }

    /// <summary>Cancel All Open Orders endpoint.</summary>
    public CancelAllOpenOrdersEndpoint<TTelemetry> CancelAllOpenOrders { get; init; }

    /// <summary>Cancel All Algo Open Orders endpoint.</summary>
    public CancelAllAlgoOpenOrdersEndpoint<TTelemetry> CancelAllAlgoOpenOrders { get; init; }

    /// <summary>Change Margin Type endpoint.</summary>
    public ChangeMarginTypeEndpoint<TTelemetry> ChangeMarginType { get; init; }

    /// <summary>Change Initial Leverage endpoint.</summary>
    public ChangeLeverageEndpoint<TTelemetry> ChangeLeverage { get; init; }

    internal Trade(BinanceRouter<TTelemetry> router)
    {
        NewOrder = new NewOrderEndpoint<TTelemetry>(router);
        CancelOrder = new CancelOrderEndpoint<TTelemetry>(router);
        ModifyOrder = new ModifyOrderEndpoint<TTelemetry>(router);
        TestOrder = new TestOrderEndpoint<TTelemetry>(router);
        NewAlgoOrder = new NewAlgoOrderEndpoint<TTelemetry>(router);
        CancelAlgoOrder = new CancelAlgoOrderEndpoint<TTelemetry>(router);
        OpenOrders = new OpenOrdersEndpoint<TTelemetry>(router);
        OpenAlgoOrders = new OpenAlgoOrdersEndpoint<TTelemetry>(router);
        CancelAllOpenOrders = new CancelAllOpenOrdersEndpoint<TTelemetry>(router);
        CancelAllAlgoOpenOrders = new CancelAllAlgoOpenOrdersEndpoint<TTelemetry>(router);
        ChangeMarginType = new ChangeMarginTypeEndpoint<TTelemetry>(router);
        ChangeLeverage = new ChangeLeverageEndpoint<TTelemetry>(router);
    }
}
