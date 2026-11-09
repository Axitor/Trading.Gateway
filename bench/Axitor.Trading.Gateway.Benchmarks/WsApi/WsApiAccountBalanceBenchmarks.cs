using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.WsApi;

/// <summary>Futures account balance V2 over the WebSocket API. Signed. Raw is the bare socket floor.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class WsApiAccountBalanceBenchmarks
{
    private WsApiBenchmarkClients _clients = null!;
    private byte[] _rawRequest = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new WsApiBenchmarkClients();
        _rawRequest = RawWsApiClient.CreateSigned("v2/account.balance", string.Empty);

        WsApiRequestResult<AccountBalanceModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor account balance");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.Account.GetBalancesAsync(),
            "Binance.Net account balance").ConfigureAwait(false);

        await _clients.Raw.ConnectAsync().ConfigureAwait(false);
        await _clients.Raw.CheckAsync(_rawRequest, "Raw account balance").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<WsApiRequestResult<AccountBalanceModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.WsApi.Account.AccountBalance.GetAsync();

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.Account.GetBalancesAsync();

    [Benchmark]
    public ValueTask<int> Raw() => _clients.Raw.RequestAsync(_rawRequest);
}
