using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.WsApi;

/// <summary>Cancel of a BTCUSDT order by the exchange order id over the WebSocket API. Signed. Raw is the bare socket floor.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class WsApiCancelOrderBenchmarks
{
    // The stub answers every cancel with the recorded one and never checks the id.
    private const long OrderId = 1;

    private WsApiBenchmarkClients _clients = null!;
    private byte[] _rawRequest = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new WsApiBenchmarkClients();
        _rawRequest = RawWsApiClient.CreateSigned("order.cancel", "\"symbol\":\"" + BenchmarkClients.SymbolName + "\",\"orderId\":1");

        WsApiRequestResult<OrderModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor cancel order");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.Trading.CancelOrderAsync(BenchmarkClients.SymbolName, OrderId),
            "Binance.Net cancel order").ConfigureAwait(false);

        await _clients.Raw.ConnectAsync().ConfigureAwait(false);
        await _clients.Raw.CheckAsync(_rawRequest, "Raw cancel order").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<WsApiRequestResult<OrderModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.WsApi.Trade.CancelOrder.CancelAsync(BenchmarkClients.Symbol, OrderId);

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.Trading.CancelOrderAsync(BenchmarkClients.SymbolName, OrderId);

    [Benchmark]
    public ValueTask<int> Raw() => _clients.Raw.RequestAsync(_rawRequest);
}
