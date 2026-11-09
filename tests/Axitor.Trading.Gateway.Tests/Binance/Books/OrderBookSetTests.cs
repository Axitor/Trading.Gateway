using System.Globalization;
using System.Net;
using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Depth;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;
using Axitor.Trading.Gateway.Tests.Fakes;

namespace Axitor.Trading.Gateway.Tests.Binance.Books;

/// <summary>Order books of a depth session, synced from REST snapshots by the set's own worker.</summary>
public sealed class OrderBookSetTests
{
    private const int Subscription = 0;
    private const string DepthPath = "/fapi/v1/depth";

    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    [Fact]
    public async Task SyncsFromSnapshotAndServesBest()
    {
        FakeHttpHandler http = new(Snapshots(100));
        using BinanceClient<NoOpTelemetryHandler> client = Client(http);
        OrderBookSet<NoOpTelemetryHandler> set = new(client, BinanceMarkets.FuturesUsdM, Options());

        set.Handler.OnData(Subscription, Header(100, 100, 99), [], []);
        await Wait.UntilAsync(() => set.IsSynced(Subscription));

        Assert.True(set.TryGetBest(Subscription, out DepthLevel bid, out DepthLevel ask, out long updateId));
        Assert.Equal(100, bid.Price);
        Assert.Equal(101, ask.Price);
        Assert.Equal(100, updateId);
        Assert.Equal($"{DepthPath}?symbol=BTCUSDT&limit=5", Assert.Single(http.Requests).PathAndQuery);
    }

    [Fact]
    public async Task SnapshotOlderThanBufferIsLoadedAgain()
    {
        // The first snapshot ends before the buffered update starts: the gap between them is unknown.
        FakeHttpHandler http = new(Snapshots(90, 100));
        using BinanceClient<NoOpTelemetryHandler> client = Client(http);
        OrderBookSet<NoOpTelemetryHandler> set = new(client, BinanceMarkets.FuturesUsdM, Options());

        set.Handler.OnData(Subscription, Header(100, 100, 99), [], []);
        await Wait.UntilAsync(() => set.IsSynced(Subscription));

        Assert.Equal(2, http.Count(DepthPath));
    }

    [Fact]
    public async Task GapResyncsFromNewSnapshot()
    {
        FakeHttpHandler http = new(Snapshots(100, 150));
        using BinanceClient<NoOpTelemetryHandler> client = Client(http);
        OrderBookSet<NoOpTelemetryHandler> set = new(client, BinanceMarkets.FuturesUsdM, Options());

        set.Handler.OnData(Subscription, Header(100, 100, 99), [], []);
        await Wait.UntilAsync(() => set.IsSynced(Subscription));

        // pu 149 does not continue u 100: updates were missed.
        set.Handler.OnData(Subscription, Header(150, 151, 149), [], []);

        Assert.False(set.IsSynced(Subscription));
        Assert.Equal(1, set.ResyncCount);

        await Wait.UntilAsync(() => set.IsSynced(Subscription));
        Assert.Equal(2, http.Count(DepthPath));
    }

    [Fact]
    public async Task UnsubscribedBookIsEmptied()
    {
        FakeHttpHandler http = new(Snapshots(100));
        using BinanceClient<NoOpTelemetryHandler> client = Client(http);
        OrderBookSet<NoOpTelemetryHandler> set = new(client, BinanceMarkets.FuturesUsdM, Options());

        set.Handler.OnData(Subscription, Header(100, 100, 99), [], []);
        await Wait.UntilAsync(() => set.IsSynced(Subscription));

        set.Handler.OnUnsubscribed(Subscription);

        Assert.False(set.IsSynced(Subscription));
        Assert.False(set.TryGetBest(Subscription, out _, out _, out _));
    }

    [Fact]
    public async Task RejectedKeyFaultsSetAndKeepsBooksOutOfSync()
    {
        FakeHttpHandler http = new((_, _) => Task.FromResult(FakeHttpHandler.Json(
            HttpStatusCode.Unauthorized, """{"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}""")));

        using BinanceClient<NoOpTelemetryHandler> client = Client(http);
        OrderBookSet<NoOpTelemetryHandler> set = new(client, BinanceMarkets.FuturesUsdM, Options());

        set.Handler.OnData(Subscription, Header(100, 100, 99), [], []);

        await Assert.ThrowsAsync<RestApiFatalException>(() => set.Completion.WaitAsync(Wait.Limit, TestContext.Current.CancellationToken));

        // A fatal set never loads a snapshot again.
        set.Handler.OnData(Subscription, Header(101, 101, 100), [], []);

        Assert.False(set.IsSynced(Subscription));
        Assert.Single(http.Requests);
    }

    [Fact]
    public void SetServesOneSessionOfItsMarket()
    {
        FakeHttpHandler http = new(Snapshots(100));
        using BinanceClient<NoOpTelemetryHandler> client = Client(http);
        OrderBookSet<NoOpTelemetryHandler> set = new(client, BinanceMarkets.FuturesUsdM, Options());

        // Another market would sync books against foreign update ids.
        Assert.Throws<InvalidOperationException>(() => set.Handler.Bind(BinanceMarkets.Spot));

        set.Handler.Bind(BinanceMarkets.FuturesUsdM);

        // A second session would share handles with the first.
        Assert.Throws<InvalidOperationException>(() => set.Handler.Bind(BinanceMarkets.FuturesUsdM));
    }

    [Theory]
    [InlineData(BinanceMarkets.None)]
    [InlineData(BinanceMarkets.FuturesUsdM | BinanceMarkets.Spot)]
    public void SetTakesExactlyOneMarket(BinanceMarkets market)
    {
        FakeHttpHandler http = new(Snapshots(100));
        using BinanceClient<NoOpTelemetryHandler> client = Client(http);

        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderBookSet<NoOpTelemetryHandler>(client, market, Options()));
    }

    private static BinanceClient<NoOpTelemetryHandler> Client(FakeHttpHandler http) =>
        TestClient.Create(http, default(NoOpTelemetryHandler), TestContext.Current.CancellationToken);

    private static OrderBookSetOptions Options() => new()
    {
        SnapshotLimit = 5,
        SnapshotRetryBaseDelay = TimeSpan.FromMilliseconds(10),
        SnapshotRetryMaxDelay = TimeSpan.FromMilliseconds(50),
    };

    /// <summary>Answers each depth request with the next snapshot id; the last one repeats.</summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Snapshots(params long[] lastUpdateIds)
    {
        int served = 0;

        return (_, _) =>
        {
            int index = Math.Min(Interlocked.Increment(ref served), lastUpdateIds.Length) - 1;
            string body = string.Create(CultureInfo.InvariantCulture,
                $$"""{"lastUpdateId":{{lastUpdateIds[index]}},"E":1,"T":1,"bids":[["100","1"],["99","2"]],"asks":[["101","1"]]}""");

            return Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, body));
        };
    }

    private static DepthUpdateHeader Header(long first, long final, long previous) =>
        new() { Symbol = _symbol, FirstUpdateId = first, FinalUpdateId = final, PreviousFinalUpdateId = previous };
}
