using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.WsApi;

/// <summary>Account information (account.status) with every symbol of the market over the WebSocket API. Signed. Raw is the bare socket floor.</summary>
/// <remarks>Binance.Net sends account.status from GetAccountInfoV1Async; its GetAccountInfoAsync sends v2/account.status.</remarks>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class WsApiAccountInfoBenchmarks
{
    private WsApiBenchmarkClients _clients = null!;
    private byte[] _rawRequest = [];

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new WsApiBenchmarkClients();
        _rawRequest = RawWsApiClient.CreateSigned("account.status", string.Empty);

        WsApiRequestResult<AccountInfoV2Model> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor account info");

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.Account.GetAccountInfoV1Async(),
            "Binance.Net account info").ConfigureAwait(false);

        await _clients.Raw.ConnectAsync().ConfigureAwait(false);
        await _clients.Raw.CheckAsync(_rawRequest, "Raw account info").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<WsApiRequestResult<AccountInfoV2Model>> Axitor() =>
        _clients.Axitor.Futures.UsdM.WsApi.Account.AccountInfo.GetAsync();

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.Account.GetAccountInfoV1Async();

    [Benchmark]
    public ValueTask<int> Raw() => _clients.Raw.RequestAsync(_rawRequest);
}
