using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.Rest;

/// <summary>Account information V2 with every symbol of the market. Signed.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class AccountInfoBenchmarks
{
    private BenchmarkClients _clients = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new BenchmarkClients();

        RestApiRequestResult<AccountInfoV2Model> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor account info");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.Account.GetAccountInfoV2Async(),
            "Binance.Net account info").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<RestApiRequestResult<AccountInfoV2Model>> Axitor() =>
        _clients.Axitor.Futures.UsdM.RestApi.Account.AccountInfoV2.GetAsync();

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.Account.GetAccountInfoV2Async();
}
