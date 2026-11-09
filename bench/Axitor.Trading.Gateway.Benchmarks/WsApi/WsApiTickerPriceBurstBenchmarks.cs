using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using BenchmarkDotNet.Attributes;
using Binance.Net.Objects;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Objects;

namespace Axitor.Trading.Gateway.Benchmarks.WsApi;

/// <summary>100 BTCUSDT price requests in flight at once over one connection. Time is per request. Raw is the bare socket floor.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
[SuppressMessage("Reliability", "CA2012", Justification = "Each stored ValueTask is awaited exactly once, then only overwritten.")]
public class WsApiTickerPriceBurstBenchmarks
{
    // Below our default in-flight limit of 256.
    private const int InFlight = 100;

    private readonly ValueTask<WsApiRequestResult<TickerPriceModel>>[] _ours = new ValueTask<WsApiRequestResult<TickerPriceModel>>[InFlight];
    private readonly Task<QueryResult<BinanceResponse<BinancePrice>>>[] _theirs = new Task<QueryResult<BinanceResponse<BinancePrice>>>[InFlight];

    private WsApiBenchmarkClients _clients = null!;
    private byte[] _rawRequest = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new WsApiBenchmarkClients();
        _rawRequest = RawWsApiClient.CreatePublic("ticker.price", "\"symbol\":\"" + BenchmarkClients.SymbolName + "\"");

        // A single call first, so the burst starts on an open connection.
        WsApiRequestResult<TickerPriceModel> first = await _clients.Axitor.Futures.UsdM.WsApi.MarketData.TickerPrice
            .GetAsync(BenchmarkClients.Symbol).ConfigureAwait(false);
        BenchmarkClients.Check(in first, "Axitor ticker price");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetPriceAsync(BenchmarkClients.SymbolName),
            "Binance.Net ticker price").ConfigureAwait(false);

        await _clients.Raw.ConnectAsync().ConfigureAwait(false);
        await _clients.Raw.CheckAsync(_rawRequest, "Raw ticker price").ConfigureAwait(false);

        // Then one whole burst of each, checking every answer of the clients.
        for (int i = 0; i < InFlight; i++)
        {
            _ours[i] = _clients.Axitor.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(BenchmarkClients.Symbol);
        }

        for (int i = 0; i < InFlight; i++)
        {
            WsApiRequestResult<TickerPriceModel> result = await _ours[i].ConfigureAwait(false);
            BenchmarkClients.Check(in result, "Axitor ticker price burst");
        }

        for (int i = 0; i < InFlight; i++)
        {
            _theirs[i] = _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetPriceAsync(BenchmarkClients.SymbolName);
        }

        for (int i = 0; i < InFlight; i++)
        {
            await BenchmarkClients.CheckAsync(_theirs[i], "Binance.Net ticker price burst").ConfigureAwait(false);
        }

        await Raw().ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true, OperationsPerInvoke = InFlight)]
    public async Task Axitor()
    {
        for (int i = 0; i < InFlight; i++)
        {
            _ours[i] = _clients.Axitor.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(BenchmarkClients.Symbol);
        }

        for (int i = 0; i < InFlight; i++)
        {
            await _ours[i].ConfigureAwait(false);
        }
    }

    [Benchmark(OperationsPerInvoke = InFlight)]
    public async Task BinanceNet()
    {
        for (int i = 0; i < InFlight; i++)
        {
            _theirs[i] = _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetPriceAsync(BenchmarkClients.SymbolName);
        }

        // Awaited one by one rather than Task.WhenAll, so neither side pays for a combinator.
        for (int i = 0; i < InFlight; i++)
        {
            await _theirs[i].ConfigureAwait(false);
        }
    }

    [Benchmark(OperationsPerInvoke = InFlight)]
    public async Task Raw()
    {
        // Reading runs alongside the sends, as on the clients.
        ValueTask receiving = _clients.Raw.ReceiveManyAsync(InFlight);

        for (int i = 0; i < InFlight; i++)
        {
            await _clients.Raw.SendAsync(_rawRequest).ConfigureAwait(false);
        }

        await receiving.ConfigureAwait(false);
    }
}
