using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;
using Axitor.Trading.Gateway.Tests.Fakes;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>The REST pipeline end to end over a fake network: bans, network repair, TTL, retries, priority slots and signing.</summary>
[Collection(IntegrationSuite.Name)]
public sealed class RestPipelineTests
{
    private const string Host = "https://fapi.binance.com";
    private const string TimePath = "/fapi/v1/time";
    private const string PingPath = "/fapi/v1/ping";
    private const string OrderPath = "/fapi/v1/order";
    private const string RetryAfterHeader = "Retry-After";
    private const string SignatureParam = "&signature=";
    private const string TimestampParam = "timestamp=";
    private const long ServerTimeMillis = 1499827319559;
    private const int RecvWindowMillis = 5000;

    // -1003 TOO_MANY_REQUESTS: Too many requests; please use the websocket for live updates.
    private const string TooManyRequestsBody = """{"code":-1003,"msg":"Too many requests; please use the websocket for live updates."}""";

    // -1003 TOO_MANY_REQUESTS: Way too many requests; IP banned until %s. Please use the websocket for live updates to avoid bans.
    private const string IpBannedBody = """{"code":-1003,"msg":"Way too many requests; IP banned until 1761638400000. Please use the websocket for live updates to avoid bans."}""";

    // The WAF answers with its own page, not an exchange error.
    private const string WafBlockedBody = "<html><body><h1>403 Forbidden</h1></body></html>";

    // -1001 DISCONNECTED: Internal error; unable to process your request. Please try again.
    private const string DisconnectedBody = """{"code":-1001,"msg":"Internal error; unable to process your request. Please try again."}""";

    private static readonly string _serverTimeBody = Encoding.UTF8.GetString(RestBodies.ServerTime);
    private static readonly string _orderBody = Encoding.UTF8.GetString(RestBodies.FuturesOrder);
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TooManyRequestsBansForRetryAfter()
    {
        int calls = 0;
        long retriedAtMillis = 0;
        Stopwatch clock = Stopwatch.StartNew();

        FakeHttpHandler http = new((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return Answer(HttpStatusCode.TooManyRequests, TooManyRequestsBody, (RetryAfterHeader, "1"));
            }

            Volatile.Write(ref retriedAtMillis, clock.ElapsedMilliseconds);
            return Answer(HttpStatusCode.OK, _serverTimeBody);
        });

        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log);

        RestApiRequestResult<ServerTimeModel> result = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(ServerTimeMillis, result.Value.ServerTime);
        Assert.Equal(1, result.Resiliency.RetryNumber);

        // The exchange asked for one second: the retry may not come sooner.
        long retriedAt = Volatile.Read(ref retriedAtMillis);
        Assert.True(1000 <= retriedAt, $"Retried after {retriedAt} ms.");

        TelemetryEvent retry = Assert.Single(log.Of(TelemetryEventKind.RestApiRequestRetryAttempt));
        Assert.Equal(RestApiRequestErrorReason.ExchangeApiBanShort, retry.RestErrors.Reason);
        Assert.Equal(1000, retry.RestResiliency.RetryDelayMillis);
    }

    [Theory]
    [InlineData(403, WafBlockedBody)]
    [InlineData(418, IpBannedBody)]
    public async Task LongBanIsFatalAndStays(int statusCode, string body)
    {
        FakeHttpHandler http = new((_, _) => Answer((HttpStatusCode)statusCode, body, (RetryAfterHeader, "1")));
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log);

        RestApiFatalException fatal = await Assert.ThrowsAsync<RestApiFatalException>(
            async () => await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token));

        Assert.Equal(RestApiRequestErrorReason.ExchangeApiBanLong, fatal.Errors.Reason);
        Assert.Equal(statusCode, fatal.Errors.StatusCode);
        Assert.Equal(RequestErrorAction.Fatal, fatal.Resiliency.Action);

        // The caller swallowed the fatal and goes on: the ban still holds, so the next request waits it out and expires unsent.
        RestApiRequestResult<ServerTimeModel> next = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(timeToLiveMs: 100, cancellationToken: Token);

        Assert.Equal(RequestOutcome.Failed, next.Outcome);
        Assert.True(next.Errors.WasDroppedByTtl);
        Assert.Single(http.Requests);
        Assert.Equal(1, log.Count(TelemetryEventKind.RestApiRequestDroppedByTtl));
    }

    [Fact]
    public async Task NetworkFailureRepairsThenRetries()
    {
        int sends = 0;
        int pings = 0;

        FakeHttpHandler http = new((request, _) =>
        {
            bool isPing = request.RequestUri!.AbsolutePath == PingPath;
            int call = isPing ? Interlocked.Increment(ref pings) : Interlocked.Increment(ref sends);

            // The first send and the first ping find the network down.
            if (call == 1)
            {
                return Task.FromException<HttpResponseMessage>(FakeHttpHandler.NetworkFailure());
            }

            return Answer(HttpStatusCode.OK, isPing ? "{}" : _serverTimeBody);
        });

        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log);

        RestApiRequestResult<ServerTimeModel> result = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(1, result.Resiliency.RetryNumber);

        // Nothing is sent again until a ping gets through.
        string[] expected = [TimePath, PingPath, PingPath, TimePath];
        Assert.Equal(expected, Paths(http));

        TelemetryEvent failure = Assert.Single(log.Of(TelemetryEventKind.RestApiRequestFailed));
        Assert.Equal(RestApiRequestErrorReason.ExceptionSocket, failure.RestErrors.Reason);
        Assert.Equal(SocketError.ConnectionRefused, failure.RestErrors.SocketError);
    }

    [Fact]
    public async Task ExpiredRequestIsNeverSent()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeHttpHandler http = new(HoldFirstRead(entered, release));
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log, engine: Slots(normal: 1, critical: 1));

        Task<RestApiRequestResult<ServerTimeModel>> holding = client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token).AsTask();
        await entered.Task.WaitAsync(Wait.Limit, Token);

        // The only normal slot is taken: the next read waits for it longer than it may live.
        Stopwatch waited = Stopwatch.StartNew();
        Task<RestApiRequestResult<ServerTimeModel>> expiring = client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(timeToLiveMs: 20, cancellationToken: Token).AsTask();
        await Wait.UntilAsync(() => 50 <= waited.ElapsedMilliseconds);
        release.SetResult();

        RestApiRequestResult<ServerTimeModel> expired = await expiring;

        Assert.Equal(RequestOutcome.Failed, expired.Outcome);
        Assert.True(expired.Errors.WasDroppedByTtl);
        Assert.Equal(RequestOutcome.Success, (await holding).Outcome);
        Assert.Single(http.Requests);
        Assert.Equal(1, log.Count(TelemetryEventKind.RestApiRequestDroppedByTtl));
    }

    [Fact]
    public async Task OrderOnServerErrorIsSentOnce()
    {
        // A 503 may come after the order was placed: sending it again could place a second one.
        FakeHttpHandler http = new((_, _) => Answer(HttpStatusCode.ServiceUnavailable, ""));
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log);

        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.NewOrder.PlaceAsync(Order(default), cancellationToken: Token);

        Assert.Equal(RequestOutcome.Failed, result.Outcome);
        Assert.Equal(RestApiRequestErrorReason.ExchangeApiRequestStatusUnknown, result.Errors.Reason);
        Assert.Equal(503, result.Errors.StatusCode);
        Assert.Equal(RequestErrorAction.Skip, result.Resiliency.Action);
        Assert.Equal(0, result.Resiliency.RetryNumber);
        Assert.Equal(HttpMethod.Post, Assert.Single(http.Requests).Method);
        Assert.Equal(0, log.Count(TelemetryEventKind.RestApiRequestRetryAttempt));
    }

    [Fact]
    public async Task ReadFailedByExchangeIsRetried()
    {
        int calls = 0;

        // -1001 means the request was not processed, so a read is safe to send again.
        FakeHttpHandler http = new((_, _) => Interlocked.Increment(ref calls) == 1
            ? Answer(HttpStatusCode.ServiceUnavailable, DisconnectedBody)
            : Answer(HttpStatusCode.OK, _serverTimeBody));

        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log);

        RestApiRequestResult<ServerTimeModel> result = await client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, result.Outcome);
        Assert.Equal(1, result.Resiliency.RetryNumber);
        Assert.Equal(2, http.Count(TimePath));

        TelemetryEvent retry = Assert.Single(log.Of(TelemetryEventKind.RestApiRequestRetryAttempt));
        Assert.Equal(RestApiRequestErrorReason.ExchangeApiRequestFailed, retry.RestErrors.Reason);
        Assert.Equal(-1001, retry.RestErrors.ExchangeCode);
    }

    [Fact]
    public async Task CriticalPassesWhenNormalSlotsAreBusy()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeHttpHandler http = new(HoldFirstRead(entered, release));
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log, engine: Slots(normal: 1, critical: 1));

        Task<RestApiRequestResult<ServerTimeModel>> holding = client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token).AsTask();
        await entered.Task.WaitAsync(Wait.Limit, Token);

        Task<RestApiRequestResult<ServerTimeModel>> queued = client.Futures.UsdM.RestApi.MarketData.ServerTime.GetAsync(cancellationToken: Token).AsTask();

        RestApiRequestResult<OrderModel> order = await client.Futures.UsdM.RestApi.Trade.NewOrder.PlaceAsync(Order(default), cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, order.Outcome);

        // A normal read never takes the critical slot: the queued one still waits.
        Assert.False(queued.IsCompleted);
        Assert.Equal(1, http.Count(TimePath));

        release.SetResult();

        Assert.Equal(RequestOutcome.Success, (await holding).Outcome);
        Assert.Equal(RequestOutcome.Success, (await queued).Outcome);
        Assert.Equal(2, http.Count(TimePath));
    }

    [Fact]
    public async Task RuleViolationIsNotChargedToLimiter()
    {
        FakeHttpHandler http = new((_, _) => Answer(HttpStatusCode.OK, _orderBody));
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log, new BinanceLimits() { FuturesRestApiLimitOrderWeightPer10s = 1 });
        SymbolFilters filters = new() { MinQuantity = 1 };

        RestApiRequestResult<OrderModel> rejected = await client.Futures.UsdM.RestApi.Trade.NewOrder.PlaceAsync(Order(filters, quantity: 0.5), cancellationToken: Token);

        Assert.Equal(RequestOutcome.Failed, rejected.Outcome);
        Assert.Equal(RestApiRequestErrorReason.OrderFiltersViolated, rejected.Errors.Reason);
        Assert.Equal(OrderRuleViolation.QuantityBelowMin, (OrderRuleViolation)rejected.Errors.RuleViolation);
        Assert.Empty(http.Requests);

        TelemetryEvent rejectedEvent = Assert.Single(log.Of(TelemetryEventKind.RestApiRequestRejected));
        Assert.Equal((int)OrderRuleViolation.QuantityBelowMin, rejectedEvent.RestErrors.RuleViolation);
        Assert.Equal(0, log.Count(TelemetryEventKind.RestApiRequestFailed));

        // The window holds one order. Had the rejected one been charged, this one would wait about 10 s and expire.
        RestApiRequestResult<OrderModel> placed = await client.Futures.UsdM.RestApi.Trade.NewOrder.PlaceAsync(Order(filters, quantity: 1), timeToLiveMs: 1000, cancellationToken: Token);

        Assert.Equal(RequestOutcome.Success, placed.Outcome);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task SignedRequestCarriesKeyAndSignature()
    {
        FakeHttpHandler http = new((_, _) => Answer(HttpStatusCode.OK, _orderBody));
        TelemetryLog log = new();
        using BinanceClient<TelemetryLogHandler> client = Client(http, log, new BinanceLimits() { RecvWindowMilliseconds = RecvWindowMillis });

        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        RestApiRequestResult<OrderModel> result = await client.Futures.UsdM.RestApi.Trade.NewOrder.PlaceAsync(Order(default), cancellationToken: Token);
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(RequestOutcome.Success, result.Outcome);

        FakeHttpRequest request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TestClient.ApiKey, request.ApiKey);
        Assert.StartsWith(OrderPath + "?", request.PathAndQuery, StringComparison.Ordinal);

        // The signature covers the whole query before it, timestamp included.
        int signatureAt = request.PathAndQuery.IndexOf(SignatureParam, StringComparison.Ordinal);
        string query = request.PathAndQuery[(OrderPath.Length + 1)..signatureAt];
        string signature = request.PathAndQuery[(signatureAt + SignatureParam.Length)..];

        Assert.Equal(Sign(query), signature);
        Assert.Contains("&recvWindow=5000&timestamp=", query, StringComparison.Ordinal);

        // The timestamp is taken at send time.
        int timestampAt = query.LastIndexOf(TimestampParam, StringComparison.Ordinal) + TimestampParam.Length;
        long timestamp = long.Parse(query[timestampAt..], CultureInfo.InvariantCulture);
        Assert.InRange(timestamp, before, after);
    }

    /// <summary>Test client whose network repair pings a path of its own.</summary>
    private static BinanceClient<TelemetryLogHandler> Client(FakeHttpHandler http, TelemetryLog log, BinanceLimits? limits = null, EngineOptions? engine = null)
    {
        BinanceOptions options = TestClient.Options();

        // The default ping URI is the server time endpoint: its requests would mix with the reads under test.
        options = options with
        {
            Uri = options.Uri with { PingURI = new Uri(Host + PingPath) },
            Limits = limits ?? options.Limits,
        };

        return TestClient.Create(http, log.Handler, Token, options, engine);
    }

    /// <summary>Test engine with the given number of slots per priority.</summary>
    private static EngineOptions Slots(int normal, int critical)
    {
        EngineOptions engine = TestClient.Engine();

        return engine with
        {
            RestApi = engine.RestApi with
            {
                Throttle = engine.RestApi.Throttle with { NormalConcurrencySlots = normal, CriticalConcurrencySlots = critical },
            },
        };
    }

    /// <summary>Holds the first server time read until released; answers everything else at once.</summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HoldFirstRead(TaskCompletionSource entered, TaskCompletionSource release)
    {
        int held = 0;

        return async (request, cancellationToken) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path == TimePath && Interlocked.Exchange(ref held, 1) == 0)
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return FakeHttpHandler.Json(HttpStatusCode.OK, path == OrderPath ? _orderBody : _serverTimeBody);
        };
    }

    private static Task<HttpResponseMessage> Answer(HttpStatusCode status, string body, params ReadOnlySpan<(string Name, string Value)> headers) =>
        Task.FromResult(FakeHttpHandler.Json(status, body, headers));

    /// <summary>Paths of every request so far, without the query, in the order they came.</summary>
    private static string[] Paths(FakeHttpHandler http)
    {
        List<string> paths = [];

        foreach (FakeHttpRequest request in http.Requests)
        {
            int queryAt = request.PathAndQuery.IndexOf('?', StringComparison.Ordinal);
            paths.Add(queryAt < 0 ? request.PathAndQuery : request.PathAndQuery[..queryAt]);
        }

        return [.. paths];
    }

    private static NewOrderRequest Order(SymbolFilters filters, double quantity = 0.01) => new()
    {
        Symbol = _symbol,
        ClientOrderId = ClientOrderId.Parse("my-order-1"),
        Filters = filters,
        Side = OrderSide.BUY,
        Type = OrderType.LIMIT,
        TimeInForce = TimeInForce.GTC,
        Quantity = quantity,
        Price = 60000,
    };

    private static string Sign(string query) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.ASCII.GetBytes(TestClient.ApiSecret), Encoding.ASCII.GetBytes(query)));
}
