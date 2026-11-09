using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.Rest;

/// <summary>Full exchange info. Ours reads symbols, status and order filters; Binance.Net reads everything.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class ExchangeInfoBenchmarks
{
    private BenchmarkClients _clients = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new BenchmarkClients();

        RestApiRequestResult<ExchangeInfoModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor exchange info");

        if (ours.Value.Symbols.Length == 0)
        {
            throw new InvalidOperationException("Axitor exchange info: no symbols.");
        }

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync(),
            "Binance.Net exchange info").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<RestApiRequestResult<ExchangeInfoModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.RestApi.MarketData.ExchangeInfo.GetAsync();

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
}
