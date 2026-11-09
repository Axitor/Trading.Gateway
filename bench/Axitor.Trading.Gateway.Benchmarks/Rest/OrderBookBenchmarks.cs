using System.Diagnostics.CodeAnalysis;
using Axitor.Trading.Gateway.Benchmarks.Clients;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using BenchmarkDotNet.Attributes;

namespace Axitor.Trading.Gateway.Benchmarks.Rest;

/// <summary>BTCUSDT order book snapshot, 1000 levels per side. AxitorBuffer writes the levels into reused buffers, as the order book set loads its snapshots.</summary>
[SuppressMessage("Design", "CA1001", Justification = "GlobalCleanup disposes the clients.")]
public class OrderBookBenchmarks
{
    private const int Limit = 1000;

    private readonly DepthLevel[] _bids = new DepthLevel[Limit];
    private readonly DepthLevel[] _asks = new DepthLevel[Limit];
    private BenchmarkClients _clients = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _clients = new BenchmarkClients();

        RestApiRequestResult<OrderBookModel> ours = await Axitor().ConfigureAwait(false);
        BenchmarkClients.Check(in ours, "Axitor order book");

        if (ours.Value.Bids.Length == 0 || ours.Value.Asks.Length == 0)
        {
            throw new InvalidOperationException("Axitor order book: a side is empty.");
        }

        RestApiRequestResult<OrderBookSnapshotModel> buffered = await AxitorBuffer().ConfigureAwait(false);
        BenchmarkClients.Check(in buffered, "Axitor order book into buffers");

        if (buffered.Value.BidCount != ours.Value.Bids.Length || buffered.Value.AskCount != ours.Value.Asks.Length)
        {
            throw new InvalidOperationException("Axitor order book into buffers: level counts differ from the array answer.");
        }

        await BenchmarkClients.CheckAsync(
            _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetOrderBookAsync(BenchmarkClients.SymbolName, Limit),
            "Binance.Net order book").ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup() => _clients.Dispose();

    [Benchmark(Baseline = true)]
    public ValueTask<RestApiRequestResult<OrderBookModel>> Axitor() =>
        _clients.Axitor.Futures.UsdM.RestApi.MarketData.OrderBook.GetAsync(BenchmarkClients.Symbol, Limit);

    [Benchmark]
    public ValueTask<RestApiRequestResult<OrderBookSnapshotModel>> AxitorBuffer() =>
        _clients.Axitor.Futures.UsdM.RestApi.MarketData.OrderBook.GetAsync(BenchmarkClients.Symbol, _bids, _asks, Limit);

    [Benchmark]
    public Task BinanceNet() =>
        _clients.BinanceNet.UsdFuturesApi.ExchangeData.GetOrderBookAsync(BenchmarkClients.SymbolName, Limit);
}
