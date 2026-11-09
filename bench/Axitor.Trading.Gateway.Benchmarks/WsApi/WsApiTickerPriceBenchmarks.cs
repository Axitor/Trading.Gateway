using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.WsApi;

/// <summary>BTCUSDT price over the WebSocket API, one request at a time. Raw is the bare socket floor.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class WsApiTickerPriceBenchmarks
{
    private WsApiBenchmarkClients _clients = null!;
    private byte[] _rawRequest = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new WsApiBenchmarkClients();
        _rawRequest = RawWsApiClient.CreatePublic("ticker.price", "\"symbol\":\"" + BenchmarkClients.SymbolName + "\"");

        // The first call of each client also waits for its connection.
        WsApiRequestResult<TickerPriceModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor ticker price");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetPriceAsync(BenchmarkClients.SymbolName),
            "Binance.Net ticker price").ConfigureAwait(false);

        await _clients.Raw.ConnectAsync().ConfigureAwait(false);
        await _clients.Raw.CheckAsync(_rawRequest, "Raw ticker price").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<WsApiRequestResult<TickerPriceModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(BenchmarkClients.Symbol);

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetPriceAsync(BenchmarkClients.SymbolName);

    [Benchmark]
    public ValueTask<int> Raw() => _clients.Raw.RequestAsync(_rawRequest);
}
