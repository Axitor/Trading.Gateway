using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Responses;
using Axitor.Trading.Gateway.Core.Models.Primitives;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Order answers of REST and the WebSocket API: an order on the exchange must never be lost to an unknown value.</summary>
public sealed class OrderParserTests
{
    private const string PlacedOrder =
        """{"clientOrderId":"testOrder","cumQty":"0","cumQuote":"0","executedQty":"0","orderId":22542179,"avgPrice":"0.00000","origQty":"10","price":"0","reduceOnly":false,"side":"BUY","positionSide":"SHORT","status":"NEW","stopPrice":"9300","closePosition":false,"symbol":"BTCUSDT","timeInForce":"GTD","type":"TRAILING_STOP_MARKET","origType":"TRAILING_STOP_MARKET","activatePrice":"9020","priceRate":"0.3","updateTime":1566818724722,"workingType":"CONTRACT_PRICE","priceProtect":false,"priceMatch":"NONE","selfTradePreventionMode":"NONE","goodTillDate":1693207680000}""";

    private const string AlgoOrderBeforeTrigger =
        """{"algoId":2146760,"clientAlgoId":"6B2I9XVcJpCjqPAJ4YoFX7","algoType":"CONDITIONAL","orderType":"TAKE_PROFIT","symbol":"BNBUSDT","side":"SELL","positionSide":"BOTH","timeInForce":"GTC","quantity":"0.01","algoStatus":"NEW","actualOrderId":"","actualPrice":"0.00000","triggerPrice":"750.000","price":"750.000","icebergQuantity":null,"tpTriggerPrice":"0.000","tpPrice":"0.000","slTriggerPrice":"0.000","slPrice":"0.000","tpOrderType":"","selfTradePreventionMode":"EXPIRE_MAKER","workingType":"CONTRACT_PRICE","priceMatch":"NONE","closePosition":false,"priceProtect":false,"reduceOnly":false,"createTime":1750485492076,"updateTime":1750485492076,"triggerTime":0,"goodTillDate":0}""";

    [Fact]
    public void ReadsPlacedOrder()
    {
        OrderModel order = BinanceOrderReader.ReadOrder(Encoding.ASCII.GetBytes(PlacedOrder));

        Assert.Equal(Symbol.Parse("BTCUSDT"), order.Symbol);
        Assert.Equal(ClientOrderId.Parse("testOrder"), order.ClientOrderId);
        Assert.Equal(22542179, order.OrderId);
        Assert.Equal(1566818724722, order.UpdateTime);
        Assert.Equal(1693207680000, order.GoodTillDate);
        Assert.Equal(10, order.Quantity);
        Assert.Equal(0, order.Price);
        Assert.Equal(0, order.AveragePrice);
        Assert.Equal(9300, order.StopPrice);
        Assert.Equal(0, order.FilledQuantity);
        Assert.Equal(0, order.FilledQuoteQuantity);
        Assert.Equal(9020, order.ActivationPrice);
        Assert.Equal(0.3, order.CallbackRate);
        Assert.Equal(OrderStatus.NEW, order.Status);
        Assert.Equal(OrderSide.BUY, order.Side);
        Assert.Equal(OrderType.TRAILING_STOP_MARKET, order.Type);
        Assert.Equal(OrderType.TRAILING_STOP_MARKET, order.OriginalType);
        Assert.Equal(TimeInForce.GTD, order.TimeInForce);
        Assert.Equal(PositionSide.SHORT, order.PositionSide);
        Assert.Equal(WorkingType.CONTRACT_PRICE, order.WorkingType);
        Assert.Equal(PriceMatch.NONE, order.PriceMatch);
        Assert.Equal(SelfTradePreventionMode.NONE, order.SelfTradePreventionMode);
        Assert.False(order.IsReduceOnly);
        Assert.False(order.IsClosePosition);
        Assert.False(order.IsPriceProtect);
    }

    [Fact]
    public void UnknownValuesDoNotLoseOrder()
    {
        byte[] body = """{"orderId":1,"symbol":"BTCUSDT","clientOrderId":"has space","status":"NEW_STATUS","type":"NEW_TYPE","side":"BUY"}"""u8.ToArray();

        OrderModel order = BinanceOrderReader.ReadOrder(body);

        Assert.Equal(1, order.OrderId);
        Assert.Equal(ClientOrderId.Empty, order.ClientOrderId);
        Assert.Equal(OrderStatus.UNKNOWN, order.Status);
        Assert.Equal(OrderType.UNKNOWN, order.Type);
        Assert.Equal(OrderSide.BUY, order.Side);
    }

    [Theory]
    [InlineData("""{"symbol":"BTCUSDT","clientOrderId":"a","status":"NEW"}""")]
    [InlineData("""{"orderId":1,"clientOrderId":"a","status":"NEW"}""")]
    [InlineData("""{"orderId":1,"symbol":"BTCUSDT","status":"NEW"}""")]
    [InlineData("""{"orderId":1,"symbol":"BTCUSDT","clientOrderId":"a"}""")]
    [InlineData("""{"orderId":"x","symbol":"BTCUSDT","clientOrderId":"a","status":"NEW"}""")]
    public void OrderWithoutIdentityIsMalformed(string text) =>
        Assert.Throws<JsonException>(() => BinanceOrderReader.ReadOrder(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void EmptyListHasNoOrders() => Assert.Empty(BinanceOrderReader.ReadOrders("[]"u8));

    [Fact]
    public void OneUnreadableOrderFailsWholeList()
    {
        // An open order left out of the list would look canceled.
        byte[] body = Encoding.ASCII.GetBytes("[" + PlacedOrder + """,{"orderId":2,"symbol":"BTCUSDT","status":"NEW"}]""");

        Assert.Throws<JsonException>(() => BinanceOrderReader.ReadOrders(body));
    }

    [Fact]
    public void ReadsAlgoOrderBeforeTrigger()
    {
        AlgoOrderModel order = BinanceOrderReader.ReadAlgoOrder(Encoding.ASCII.GetBytes(AlgoOrderBeforeTrigger));

        Assert.Equal(Symbol.Parse("BNBUSDT"), order.Symbol);
        Assert.Equal(ClientOrderId.Parse("6B2I9XVcJpCjqPAJ4YoFX7"), order.ClientAlgoId);
        Assert.Equal(2146760, order.AlgoId);
        Assert.Equal(1750485492076, order.CreateTime);
        Assert.Equal(1750485492076, order.UpdateTime);
        Assert.Equal(0.01, order.Quantity);
        Assert.Equal(750, order.Price);
        Assert.Equal(750, order.TriggerPrice);
        Assert.Equal(AlgoStatus.NEW, order.Status);
        Assert.Equal(AlgoType.CONDITIONAL, order.AlgoType);
        Assert.Equal(OrderType.TAKE_PROFIT, order.Type);
        Assert.Equal(OrderSide.SELL, order.Side);
        Assert.Equal(PositionSide.BOTH, order.PositionSide);
        Assert.Equal(TimeInForce.GTC, order.TimeInForce);
        Assert.Equal(WorkingType.CONTRACT_PRICE, order.WorkingType);
        Assert.Equal(SelfTradePreventionMode.EXPIRE_MAKER, order.SelfTradePreventionMode);

        // Not triggered yet: the exchange sends an empty id and a zero price.
        Assert.Equal(0, order.ActualOrderId);
        Assert.Equal(0, order.ActualPrice);
        Assert.Equal(0, order.TriggerTime);
    }

    [Fact]
    public void EmptyAlgoListHasNoOrders() => Assert.Empty(BinanceOrderReader.ReadAlgoOrders("[]"u8));

    [Fact]
    public void ReadsAlgoCancel()
    {
        AlgoOrderCancelModel cancel = BinanceOrderReader.ReadAlgoOrderCancel(
            """{"algoId":2146760,"clientAlgoId":"6B2I9XVcJpCjqPAJ4YoFX7","code":"200","msg":"success"}"""u8);

        Assert.Equal(2146760, cancel.AlgoId);
        Assert.Equal(ClientOrderId.Parse("6B2I9XVcJpCjqPAJ4YoFX7"), cancel.ClientAlgoId);
    }

    [Fact]
    public void AlgoCancelWithoutIdIsMalformed() =>
        Assert.Throws<JsonException>(() => BinanceOrderReader.ReadAlgoOrderCancel("""{"clientAlgoId":"a","code":"200"}"""u8));

    [Theory]
    [InlineData("""{"code":200,"msg":"success"}""")]
    [InlineData("""{"code":"200","msg":"The operation of cancel all open order is done."}""")]
    [InlineData("""{"msg":"success"}""")]
    public async Task AcknowledgeAcceptsSuccess(string text)
    {
        using MemoryStream body = new(Encoding.ASCII.GetBytes(text));

        await default(AcknowledgeParser).ParseAsync(body, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("""{"code":201,"msg":"success"}""")]
    [InlineData("""{"code":"x","msg":"success"}""")]
    [InlineData("[]")]
    public async Task AcknowledgeRejectsAnythingElse(string text)
    {
        using MemoryStream body = new(Encoding.ASCII.GetBytes(text));

        await Assert.ThrowsAsync<JsonException>(() => default(AcknowledgeParser).ParseAsync(body, TestContext.Current.CancellationToken).AsTask());
    }
}
