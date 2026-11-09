using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Klines;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using BenchmarkDotNet.Attributes;
using Binance.Net.Enums;

namespace Axitor.Trading.Gateway.Benchmarks.Rest;

/// <summary>BTCUSDT 1m klines. Ours writes into a reused buffer; Binance.Net builds a model per candle.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class KlinesBenchmarks
{
    private BenchmarkClients _clients = null!;
    private Kline[] _buffer = [];

    [Params(500, 1500)]
    public int Count { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new BenchmarkClients();
        _buffer = new Kline[Count];

        RestApiRequestResult<int> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor klines");

        if (ours.Value != Count)
        {
            throw new InvalidOperationException($"Axitor klines: {ours.Value} candles, expected {Count}.");
        }

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetKlinesAsync(BenchmarkClients.SymbolName, KlineInterval.OneMinute, limit: Count),
            "Binance.Net klines").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<RestApiRequestResult<int>> Axitor() =>
        _clients.Axitor.Futures.UsdM.RestApi.MarketData.KlinesData.GetAsync(BenchmarkClients.Symbol, BinanceInterval.OneMinute, _buffer);

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetKlinesAsync(BenchmarkClients.SymbolName, KlineInterval.OneMinute, limit: Count);
}
