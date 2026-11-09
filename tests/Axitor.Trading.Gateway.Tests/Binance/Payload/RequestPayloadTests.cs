using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Authentication;
using Axitor.Trading.Gateway.Binance.Dialects;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.CancelOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.ModifyOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewAlgoOrder;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Trade.NewOrder;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Wire;

namespace Axitor.Trading.Gateway.Tests.Binance.Payload;

/// <summary>Exact bytes of requests on the wire. Any change in order, spelling or a left-out field breaks them.</summary>
public sealed class RequestPayloadTests
{
    private const int BufferBytes = 4096;
    private const int RecvWindowMillis = 5000;
    private const long Timestamp = 1761638400000;
    private const long RequestId = 7;

    private static readonly byte[] _host = "https://fapi.binance.com"u8.ToArray();
    private static readonly byte[] _apiKey = "test-key"u8.ToArray();
    private static readonly byte[] _secret = "test-secret"u8.ToArray();

    [Fact]
    public void SignedRestOrderMatchesTheWire()
    {
        NewOrderPayload payload = new() { Host = _host, Request = LimitOrder() };
        BinanceRestApiAuthenticator auth = new(_secret, RecvWindowMillis);

        const string query = "symbol=BTCUSDT&side=BUY&positionSide=BOTH&type=LIMIT&timeInForce=GTC&quantity=0.01&price=60000&reduceOnly=true"
            + "&newClientOrderId=my-order-1&newOrderRespType=RESULT&selfTradePreventionMode=EXPIRE_MAKER&recvWindow=5000&timestamp=1761638400000";

        Assert.Equal("https://fapi.binance.com/fapi/v1/order?" + query + "&signature=" + Sign(query), WriteRest(payload, auth));
    }

    [Fact]
    public void TestOrderGoesToTestPathWithOnlyFieldsSet()
    {
        NewOrderRequest request = new()
        {
            Symbol = Symbol.Parse("ETHUSDT"),
            ClientOrderId = ClientOrderId.Parse("x-1"),
            Filters = default,
            Side = OrderSide.SELL,
            Type = OrderType.MARKET,
            Quantity = 1.5,
        };

        NewOrderPayload payload = new() { Host = _host, Request = request, IsTest = true };

        Assert.Equal(
            "https://fapi.binance.com/fapi/v1/order/test?symbol=ETHUSDT&side=SELL&positionSide=BOTH&type=MARKET&quantity=1.5&newClientOrderId=x-1",
            WriteRest(payload, default(NoOpAuthenticator)));
    }

    [Fact]
    public void CancelFindsOrderByOneId()
    {
        Symbol symbol = Symbol.Parse("BTCUSDT");
        ClientOrderId clientOrderId = ClientOrderId.Parse("my-order-1");

        CancelOrderPayload byOrderId = new() { Host = _host, Symbol = symbol, OrderId = 42, ClientOrderId = clientOrderId };
        CancelOrderPayload byClientId = new() { Host = _host, Symbol = symbol, ClientOrderId = clientOrderId };

        Assert.Equal("https://fapi.binance.com/fapi/v1/order?symbol=BTCUSDT&orderId=42", WriteRest(byOrderId, default(NoOpAuthenticator)));
        Assert.Equal("https://fapi.binance.com/fapi/v1/order?symbol=BTCUSDT&origClientOrderId=my-order-1", WriteRest(byClientId, default(NoOpAuthenticator)));
    }

    [Fact]
    public void ModifySendsOnlyWhatChanges()
    {
        Symbol symbol = Symbol.Parse("BTCUSDT");

        ModifyOrderPayload byClientId = new()
        {
            Host = _host,
            Symbol = symbol,
            ClientOrderId = ClientOrderId.Parse("my-order-1"),
            Side = OrderSide.BUY,
            Quantity = 0.02,
            Price = 59000,
        };

        ModifyOrderPayload byOrderId = new()
        {
            Host = _host,
            Symbol = symbol,
            OrderId = 42,
            Side = OrderSide.BUY,
            Quantity = 0.02,
            Price = 59000,
            IsReduceOnly = true,
            ModifyId = 7,
        };

        Assert.Equal(
            "https://fapi.binance.com/fapi/v1/order?symbol=BTCUSDT&origClientOrderId=my-order-1&side=BUY&quantity=0.02&price=59000",
            WriteRest(byClientId, default(NoOpAuthenticator)));

        Assert.Equal(
            "https://fapi.binance.com/fapi/v1/order?symbol=BTCUSDT&orderId=42&side=BUY&quantity=0.02&price=59000&reduceOnly=true&modifyId=7",
            WriteRest(byOrderId, default(NoOpAuthenticator)));
    }

    [Fact]
    public void ClosePositionStopSendsNeitherQuantityNorReduceOnly()
    {
        NewAlgoOrderRequest request = new()
        {
            Symbol = Symbol.Parse("BTCUSDT"),
            ClientAlgoId = ClientOrderId.Parse("my-stop-1"),
            Filters = default,
            Side = OrderSide.SELL,
            Type = OrderType.STOP_MARKET,
            TriggerPrice = 55000,
            WorkingType = WorkingType.MARK_PRICE,
            IsClosePosition = true,
            Quantity = 0.5,
            IsReduceOnly = true,
        };

        NewAlgoOrderPayload payload = new() { Host = _host, Request = request };

        Assert.Equal(
            "https://fapi.binance.com/fapi/v1/algoOrder?algoType=CONDITIONAL&symbol=BTCUSDT&side=SELL&positionSide=BOTH&type=STOP_MARKET"
                + "&closePosition=true&triggerPrice=55000&workingType=MARK_PRICE&clientAlgoId=my-stop-1",
            WriteRest(payload, default(NoOpAuthenticator)));
    }

    [Fact]
    public void WsApiOrderParamsGoInNameOrder()
    {
        const string expected = """{"id":7,"method":"order.place","params":{"newClientOrderId":"my-order-1","newOrderRespType":"RESULT","positionSide":"BOTH","price":"60000","quantity":"0.01","reduceOnly":"true","selfTradePreventionMode":"EXPIRE_MAKER","side":"BUY","symbol":"BTCUSDT","timeInForce":"GTC","type":"LIMIT"}}""";

        Assert.Equal(expected, WriteOrderPlace(default(NoOpWsApiAuthenticator)));
    }

    [Fact]
    public void SignedWsApiRequestIsSignedOverItsSortedParams()
    {
        string frame = WriteOrderPlace(new BinanceWsApiAuthenticator(_apiKey, _secret, RecvWindowMillis));

        using JsonDocument document = JsonDocument.Parse(frame);
        JsonElement parameters = document.RootElement.GetProperty("params");

        StringBuilder query = new();
        string? previous = null;
        string? signature = null;

        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            // The signature comes last: nothing may follow it.
            Assert.Null(signature);

            if (property.Name == "signature")
            {
                signature = property.Value.GetString();
                continue;
            }

            Assert.True(previous is null || string.CompareOrdinal(previous, property.Name) < 0, $"{property.Name} is out of order after {previous}.");
            previous = property.Name;

            string value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
            query.Append(query.Length == 0 ? "" : "&").Append(property.Name).Append('=').Append(value);
        }

        Assert.Equal("test-key", parameters.GetProperty("apiKey").GetString());
        Assert.Equal(RecvWindowMillis, parameters.GetProperty("recvWindow").GetInt32());
        Assert.True(0 < parameters.GetProperty("timestamp").GetInt64());
        Assert.Equal(Sign(query.ToString()), signature);
    }

    private static NewOrderRequest LimitOrder() => new()
    {
        Symbol = Symbol.Parse("BTCUSDT"),
        ClientOrderId = ClientOrderId.Parse("my-order-1"),
        Filters = default,
        Side = OrderSide.BUY,
        Type = OrderType.LIMIT,
        TimeInForce = TimeInForce.GTC,
        Quantity = 0.01,
        Price = 60000,
        IsReduceOnly = true,
        ResponseType = OrderResponseType.RESULT,
        SelfTradePreventionMode = SelfTradePreventionMode.EXPIRE_MAKER,
    };

    /// <summary>The request line as the REST transport builds it: payload, then timestamp, then signature.</summary>
    private static string WriteRest<TPayload, TAuth>(TPayload payload, TAuth auth)
        where TPayload : struct, IPayloadWriter
        where TAuth : struct, IRequestAuthenticator
    {
        byte[] buffer = new byte[BufferBytes];
        PayloadBuilder builder = new(buffer);

        payload.Write(ref builder);
        auth.AppendTimestamp(ref builder, Timestamp);
        auth.AppendSignature(ref builder);

        return Encoding.ASCII.GetString(builder.AsSpan());
    }

    /// <summary>The order.place frame as the WebSocket API transport writes it.</summary>
    private static string WriteOrderPlace<TAuth>(TAuth auth) where TAuth : struct, IWsApiAuthenticator
    {
        Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewOrder.NewOrderPayload request = new() { Request = LimitOrder() };

        byte[] buffer = new byte[BufferBytes];
        PayloadBuilder builder = new(buffer);

        new WsApiRequestFrame<BinanceDialectWsApi, Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Trade.NewOrder.NewOrderPayload, TAuth>(request, auth)
            .Write(ref builder, RequestId);

        return Encoding.ASCII.GetString(builder.AsSpan());
    }

    private static string Sign(string query) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(query)));
}
