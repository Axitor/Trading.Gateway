using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.WsApi;

/// <summary>A LIMIT GTC order on BTCUSDT over the WebSocket API. Ours rounds and checks it by the symbol filters before sending. Signed. Raw is the bare socket floor.</summary>
/// <remarks>Binance.Net enums are written with global::, since Binance here resolves to our own connector namespace.</remarks>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class WsApiNewOrderBenchmarks
{
    private const string ClientOrderIdText = "bench-order-1";
    private const double Quantity = 0.01;
    private const double Price = 60000;
    private const decimal QuantityDecimal = 0.01m;
    private const decimal PriceDecimal = 60000m;

    private WsApiBenchmarkClients _clients = null!;
    private NewOrderRequest _request;
    private byte[] _rawRequest = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new WsApiBenchmarkClients();
        _rawRequest = RawWsApiClient.CreateSigned(
            "order.place",
            "\"symbol\":\"" + BenchmarkClients.SymbolName + "\",\"side\":\"BUY\",\"type\":\"LIMIT\",\"timeInForce\":\"GTC\"," +
            "\"quantity\":\"0.01\",\"price\":\"60000\",\"newClientOrderId\":\"" + ClientOrderIdText + "\"");

        // The filters come from REST, as an application would load them at start.
        RestApiRequestResult<ExchangeInfoModel> info = await _clients.Axitor.Futures.UsdM.RestApi.MarketData.ExchangeInfo.GetAsync().ConfigureAwait(false);
        BenchmarkClients.Check(in info, "Axitor exchange info");

        _request = new NewOrderRequest()
        {
            Symbol = BenchmarkClients.Symbol,
            ClientOrderId = ClientOrderId.Parse(ClientOrderIdText),
            Filters = FindFilters(info.Value.Symbols),
            Side = OrderSide.BUY,
            Type = OrderType.LIMIT,
            TimeInForce = TimeInForce.GTC,
            Quantity = Quantity,
            Price = Price,
        };

        WsApiRequestResult<OrderModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor new order");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.Trading.PlaceOrderAsync(
                BenchmarkClients.SymbolName,
                global::Binance.Net.Enums.OrderSide.Buy,
                global::Binance.Net.Enums.FuturesOrderType.Limit,
                quantity: QuantityDecimal,
                price: PriceDecimal,
                timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                newClientOrderId: ClientOrderIdText),
            "Binance.Net new order").ConfigureAwait(false);

        await _clients.Raw.ConnectAsync().ConfigureAwait(false);
        await _clients.Raw.CheckAsync(_rawRequest, "Raw new order").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<WsApiRequestResult<OrderModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(in _request);

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.Trading.PlaceOrderAsync(
            BenchmarkClients.SymbolName,
            global::Binance.Net.Enums.OrderSide.Buy,
            global::Binance.Net.Enums.FuturesOrderType.Limit,
            quantity: QuantityDecimal,
            price: PriceDecimal,
            timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled,
            newClientOrderId: ClientOrderIdText);

    [Benchmark]
    public ValueTask<int> Raw() => _clients.Raw.RequestAsync(_rawRequest);

    private static SymbolFilters FindFilters(ExchangeSymbol[] symbols)
    {
        for (int i = 0; i < symbols.Length; i++)
        {
            if (symbols[i].Symbol == BenchmarkClients.Symbol)
            {
                return symbols[i].Filters;
            }
        }

        throw new InvalidOperationException("The recorded exchange info has no " + BenchmarkClients.SymbolName + ".");
    }
}
