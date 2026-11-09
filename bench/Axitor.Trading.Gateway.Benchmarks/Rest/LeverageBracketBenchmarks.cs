using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.Rest;

/// <summary>Leverage brackets of every symbol. Signed.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class LeverageBracketBenchmarks
{
    private BenchmarkClients _clients = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new BenchmarkClients();

        RestApiRequestResult<LeverageBracketModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor leverage brackets");

        if (ours.Value.Symbols.Length == 0)
        {
            throw new InvalidOperationException("Axitor leverage brackets: no symbols.");
        }

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.Account.GetBracketsAsync(),
            "Binance.Net leverage brackets").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<RestApiRequestResult<LeverageBracketModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.RestApi.Account.LeverageBracket.GetAllAsync();

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.Account.GetBracketsAsync();
}
