using System.Net;
using Axitor.Trading.Gateway.Binance.Handlers;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Sbe;
using Axitor.Trading.Gateway.Binance.Models.Streams;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Tests.Fakes;

namespace Axitor.Trading.Gateway.Tests.Binance.Guards;

/// <summary>A market left out of the options fails at once, rather than waiting for a connection that never opens.</summary>
public sealed class MarketGuardTests
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    [Fact]
    public void StreamSessionOnDisabledMarketThrows()
    {
        using BinanceClient<NoOpTelemetryHandler> client = Client();
        int capacity = client.StreamSessionCapacity;

        Assert.Throws<InvalidOperationException>(() => client.Futures.UsdM.WsStreams.BookTicker.CreateSession(new LatestCache<BookTickerModel>(capacity).Handler));
        Assert.Throws<InvalidOperationException>(() => client.Spot.WsStreams.AggTrade.CreateSession(new ChannelSink<AggTradeModel>(16, capacity).Handler));
        Assert.Throws<InvalidOperationException>(() => client.Futures.CoinM.WsStreams.Depth.CreateSession(new DepthLog().Handler));
    }

    [Fact]
    public async Task WsApiRequestOnDisabledMarketThrows()
    {
        using BinanceClient<NoOpTelemetryHandler> client = Client();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(_symbol, cancellationToken: cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.Spot.WsApi.MarketData.TickerPrice.GetAsync(_symbol, cancellationToken: cancellationToken));
    }

    [Fact]
    public void UserDataOnDisabledMarketThrows()
    {
        using BinanceClient<NoOpTelemetryHandler> client = Client();

        Assert.Throws<InvalidOperationException>(() => client.Futures.UsdM.WsStreams.UserData.CreateSession(new UserDataLog().Handler));
        Assert.Throws<InvalidOperationException>(() => client.Spot.WsApi.UserData.CreateSession(new SpotUserDataLog().Handler));
    }

    [Fact]
    public void SbeSessionWithoutSpotStreamsThrows()
    {
        using BinanceClient<NoOpTelemetryHandler> client = Client();

        Assert.Throws<InvalidOperationException>(() => client.Spot.SbeStreams.BestBidAsk.CreateSession(new LatestCache<SbeBestBidAskModel>(client.StreamSessionCapacity).Handler));
    }

    [Fact]
    public void RefusedDepthSessionLeavesOrderBookSetFree()
    {
        using BinanceClient<NoOpTelemetryHandler> client = Client();
        OrderBookSet<NoOpTelemetryHandler> futures = new(client, BinanceMarkets.FuturesUsdM);
        OrderBookSet<NoOpTelemetryHandler> spot = new(client, BinanceMarkets.Spot);

        Assert.Throws<InvalidOperationException>(() => client.Futures.UsdM.WsStreams.Depth.CreateSession(futures.Handler));
        Assert.Throws<InvalidOperationException>(() => client.Spot.SbeStreams.Depth.CreateSession(spot.Handler));

        // Refused before binding: each set can still serve its session once the market is enabled.
        futures.Handler.Bind(BinanceMarkets.FuturesUsdM);
        spot.Handler.Bind(BinanceMarkets.Spot);
    }

    private static BinanceClient<NoOpTelemetryHandler> Client() =>
        TestClient.Create(
            new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Json(HttpStatusCode.OK, "{}"))),
            default(NoOpTelemetryHandler),
            TestContext.Current.CancellationToken);
}
