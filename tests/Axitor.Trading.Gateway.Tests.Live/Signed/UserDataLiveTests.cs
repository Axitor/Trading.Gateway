using System.Collections.Concurrent;
using Axitor.Trading.Gateway.Binance.Abstractions;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ChangeLeverage;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Binance.Session.Streams;
using Axitor.Trading.Gateway.Binance.Session.WsApi;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live.Signed;

/// <summary>User data sessions of every market on mainnet. No order is placed; the only event made on purpose is a leverage change.</summary>
public sealed class UserDataLiveTests
{
    private const BinanceUserDataEvents UsdMEvents =
        BinanceUserDataEvents.OrderTradeUpdate
        | BinanceUserDataEvents.AccountUpdate
        | BinanceUserDataEvents.TradeLite
        | BinanceUserDataEvents.AlgoUpdate
        | BinanceUserDataEvents.AccountConfigUpdate
        | BinanceUserDataEvents.MarginCall;

    // COIN-M sends no TradeLite or AlgoUpdate.
    private const BinanceUserDataEvents CoinMEvents =
        BinanceUserDataEvents.OrderTradeUpdate
        | BinanceUserDataEvents.AccountUpdate
        | BinanceUserDataEvents.AccountConfigUpdate
        | BinanceUserDataEvents.MarginCall;

    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(BinanceMarkets.FuturesUsdM)]
    [InlineData(BinanceMarkets.FuturesCoinM)]
    public async Task FuturesSessionSubscribesOnceAndUnsubscribes(BinanceMarkets market)
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        UserDataLog userData = new(log);

        // The listen key comes over the WebSocket API, the events over the stream connection.
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true, wsApiMarkets: market, streamMarkets: market);

        (BinanceUserDataSession session, BinanceUserDataEvents events) = market switch
        {
            BinanceMarkets.FuturesUsdM => (client.Futures.UsdM.WsStreams.UserData.CreateSession(new FuturesHandler(userData)), UsdMEvents),
            BinanceMarkets.FuturesCoinM => (client.Futures.CoinM.WsStreams.UserData.CreateSession(new FuturesHandler(userData)), CoinMEvents),
            _ => throw new ArgumentOutOfRangeException(nameof(market)),
        };

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(events, Token));

        // One subscription per session: a second call takes no new key.
        Assert.Equal(WsStreamSubscribeStatus.AlreadySubscribed, await session.SubscribeAsync(events, Token));

        // Checked before unsubscribe: the tail after it comes as unknown messages.
        log.AssertContractKept();

        Assert.True(await session.UnsubscribeAsync(Token));
        Assert.Equal(0, userData.Breaks);
        Assert.False(session.Completion.IsFaulted);
    }

    [Fact]
    public async Task LeverageChangeComesAsAccountConfigUpdate()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        UserDataLog userData = new(log);
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(
            log, signed: true, wsApiMarkets: BinanceMarkets.FuturesUsdM, streamMarkets: BinanceMarkets.FuturesUsdM);
        BinanceUserDataSession session = client.Futures.UsdM.WsStreams.UserData.CreateSession(new FuturesHandler(userData));

        RestApiRequestResult<SymbolConfigModel> config = await client.Futures.UsdM.RestApi.Account.SymbolConfig.GetAsync(_symbol, cancellationToken: Token);
        Assert.Equal(RequestOutcome.Success, config.Outcome);

        int original = Assert.Single(config.Value.Symbols).Leverage;

        // A neighbor value is always allowed: 1 is the floor, and the current one already fits the first bracket.
        int changed = original == 1 ? 2 : original - 1;

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(BinanceUserDataEvents.AccountConfigUpdate, Token));

        RestApiRequestResult<ChangeLeverageModel> restore;

        try
        {
            RestApiRequestResult<ChangeLeverageModel> change = await ChangeLeverageAsync(client, changed, Token);

            Assert.Equal(RequestOutcome.Success, change.Outcome);
            Assert.Equal(_symbol, change.Value.Symbol);
            Assert.Equal(changed, change.Value.Leverage);

            await LiveEnvironment.UntilAsync(() => userData.TryFindLeverage(_symbol, changed, out _));
        }
        finally
        {
            // Not the test token: a canceled test must still give the account its leverage back.
            restore = await ChangeLeverageAsync(client, original, CancellationToken.None);
            log.Report($"Leverage set back to {original}: {restore.Outcome}");
        }

        Assert.Equal(RequestOutcome.Success, restore.Outcome);
        await LiveEnvironment.UntilAsync(() => userData.TryFindLeverage(_symbol, original, out _));

        Assert.True(userData.TryFindLeverage(_symbol, changed, out AccountConfigUpdateModel update));
        Assert.False(update.IsMultiAssetsModeChange);
        Assert.True(0 < update.EventTime && 0 < update.TransactionTime);

        // Checked before unsubscribe: the tail after it comes as unknown messages.
        log.AssertContractKept();

        Assert.True(await session.UnsubscribeAsync(Token));
        Assert.Equal(0, userData.Breaks);
    }

    [Fact]
    public async Task SpotSessionSubscribesAndUnsubscribes()
    {
        LiveEnvironment.RequireKeys();
        LiveTelemetryLog log = new();
        UserDataLog userData = new(log);
        using BinanceClient<LiveTelemetry> client = LiveEnvironment.CreateClient(log, signed: true, wsApiMarkets: BinanceMarkets.Spot);
        BinanceSpotUserDataSession session = client.Spot.WsApi.UserData.CreateSession(new SpotHandler(userData));

        Assert.Equal(WsStreamSubscribeStatus.Subscribed, await session.SubscribeAsync(Token));
        log.AssertContractKept();

        Assert.True(await session.UnsubscribeAsync(Token));
        Assert.Equal(0, userData.Breaks);
        Assert.False(session.Completion.IsFaulted);
    }

    private static ValueTask<RestApiRequestResult<ChangeLeverageModel>> ChangeLeverageAsync(
        BinanceClient<LiveTelemetry> client,
        int leverage,
        CancellationToken cancellationToken) =>
        client.Futures.UsdM.RestApi.Trade.ChangeLeverage.ChangeAsync(_symbol, leverage, cancellationToken: cancellationToken);

    /// <summary>Writes what reached a user data handler to the test output, keeps leverage changes and counts the breaks.</summary>
    private sealed class UserDataLog
    {
        private readonly LiveTelemetryLog _output;
        private readonly ConcurrentQueue<AccountConfigUpdateModel> _configUpdates = new();
        private int _breaks;

        public UserDataLog(LiveTelemetryLog output) => _output = output;

        public int Breaks => Volatile.Read(ref _breaks);

        public void Report(string line) => _output.Report(line);

        public void Break(string line)
        {
            Interlocked.Increment(ref _breaks);
            _output.Report(line);
        }

        public void AddConfigUpdate(in AccountConfigUpdateModel model)
        {
            _configUpdates.Enqueue(model);
            _output.Report($"User data: account config update, {model.Symbol} leverage {model.Leverage}");
        }

        /// <summary>Finds a change of the symbol's leverage to the given value.</summary>
        public bool TryFindLeverage(Symbol symbol, int leverage, out AccountConfigUpdateModel update)
        {
            foreach (AccountConfigUpdateModel item in _configUpdates)
            {
                if (item.Symbol == symbol && item.Leverage == leverage)
                {
                    update = item;
                    return true;
                }
            }

            update = default;
            return false;
        }
    }

    private readonly struct FuturesHandler : IBinanceUserDataHandler
    {
        private readonly UserDataLog _log;

        public FuturesHandler(UserDataLog log) => _log = log;

        public void OnOrderUpdate(in OrderUpdateModel model) => _log.Report("User data: order update");

        public void OnAccountUpdate(in AccountUpdateHeader header, ReadOnlySpan<AccountUpdateBalance> balances, ReadOnlySpan<AccountUpdatePosition> positions) =>
            _log.Report("User data: account update");

        public void OnTradeLite(in TradeLiteModel model) => _log.Report("User data: trade lite");

        public void OnAlgoUpdate(in AlgoUpdateModel model) => _log.Report("User data: algo update");

        public void OnAccountConfigUpdate(in AccountConfigUpdateModel model) => _log.AddConfigUpdate(in model);

        public void OnMarginCall(in MarginCallHeader header, ReadOnlySpan<MarginCallPosition> positions) => _log.Report("User data: margin call");

        public void OnDisconnected(WsDisconnectReason reason, bool isFatal) => _log.Break($"User data stopped: {reason}, fatal {isFatal}");

        public void OnReconnected(in WsStreamGap gap) => _log.Report($"User data is back after {gap.EndUtcMillis - gap.StartUtcMillis} ms, {gap.Reason}");
    }

    private readonly struct SpotHandler : IBinanceSpotUserDataHandler
    {
        private readonly UserDataLog _log;

        public SpotHandler(UserDataLog log) => _log = log;

        public void OnExecutionReport(in SpotExecutionReportModel model) => _log.Report("Spot user data: execution report");

        public void OnAccountPosition(in SpotAccountPositionHeader header, ReadOnlySpan<SpotAccountBalance> balances) => _log.Report("Spot user data: account position");

        public void OnBalanceUpdate(in SpotBalanceUpdateModel model) => _log.Report("Spot user data: balance update");

        public void OnListStatus(in SpotListStatusHeader header, ReadOnlySpan<SpotListStatusOrder> orders) => _log.Report("Spot user data: list status");

        public void OnExternalLockUpdate(in SpotExternalLockUpdateModel model) => _log.Report("Spot user data: external lock update");

        public void OnDisconnected(WsDisconnectReason reason, bool isFatal) => _log.Break($"Spot user data stopped: {reason}, fatal {isFatal}");

        public void OnReconnected(in WsStreamGap gap) => _log.Report($"Spot user data is back after {gap.EndUtcMillis - gap.StartUtcMillis} ms, {gap.Reason}");
    }
}
