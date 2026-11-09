using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.EndPoints.Spot.UserDataStream.Subscription;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.UserData;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Spot user data: a lost balance change leaves the balance wrong, so an unreadable required field stops the session.</summary>
public sealed class SpotUserDataParserTests
{
    [Fact]
    public void ReadsNewOrderWithEmptyFieldsAsDefaults()
    {
        Assert.Equal(WsDispatchResult.Handled, SpotExecutionReportParser.Read(Bytes(UserDataFrames.SpotExecutionReport), out SpotExecutionReportModel model));

        Assert.Equal(Symbol.Parse("ETHBTC"), model.Symbol);
        Assert.Equal(ClientOrderId.Parse("mUvoqJxFIILMdfAW5iGSOW"), model.ClientOrderId);
        Assert.Equal(4293153, model.OrderId);
        Assert.Equal(8641984, model.ExecutionId);
        Assert.Equal(1499405658658, model.EventTime);
        Assert.Equal(1499405658657, model.TransactionTime);
        Assert.Equal(1499405658657, model.CreateTime);
        Assert.Equal(1499405658657, model.WorkingTime);
        Assert.Equal(1, model.Quantity);
        Assert.Equal(0.1026441, model.Price);
        Assert.Equal(SpotOrderType.LIMIT, model.Type);
        Assert.Equal(OrderSide.BUY, model.Side);
        Assert.Equal(TimeInForce.GTC, model.TimeInForce);
        Assert.Equal(SpotExecutionType.NEW, model.ExecutionType);
        Assert.Equal(SpotOrderStatus.NEW, model.Status);
        Assert.Equal(SpotSelfTradePreventionMode.NONE, model.SelfTradePreventionMode);
        Assert.True(model.IsWorking);
        Assert.False(model.IsMaker);

        // Not a list, not a fill, nothing canceled, not expired.
        Assert.Equal(-1, model.OrderListId);
        Assert.Equal(-1, model.TradeId);
        Assert.Null(model.RejectReason);
        Assert.True(model.CommissionAsset.IsEmpty);
        Assert.True(model.OriginalClientOrderId.IsEmpty);
        Assert.Equal(SpotExpiryReason.NONE, model.ExpiryReason);
    }

    [Fact]
    public void ReadsFill()
    {
        Assert.Equal(WsDispatchResult.Handled, SpotExecutionReportParser.Read(Bytes(UserDataFrames.SpotExecutionFill), out SpotExecutionReportModel model));

        Assert.Equal(ClientOrderId.Parse("my-order-2"), model.ClientOrderId);
        Assert.Equal(SpotExecutionType.TRADE, model.ExecutionType);
        Assert.Equal(SpotOrderStatus.PARTIALLY_FILLED, model.Status);
        Assert.Equal(OrderSide.SELL, model.Side);
        Assert.Equal(77, model.TradeId);
        Assert.Equal(0.5, model.LastFilledQuantity);
        Assert.Equal(0.5, model.FilledQuantity);
        Assert.Equal(0.05, model.LastFilledPrice);
        Assert.Equal(0.025, model.FilledQuoteQuantity);
        Assert.Equal(0.025, model.LastFilledQuoteQuantity);
        Assert.Equal(0.00012, model.Commission);
        Assert.Equal(Symbol.Parse("BNB"), model.CommissionAsset);
        Assert.Equal(SpotSelfTradePreventionMode.EXPIRE_MAKER, model.SelfTradePreventionMode);
        Assert.True(model.IsMaker);
        Assert.False(model.IsWorking);
    }

    [Fact]
    public void RejectReasonIsKeptAsText()
    {
        string frame = UserDataFrames.SpotExecutionReport.Replace("\"r\":\"NONE\"", "\"r\":\"INSUFFICIENT_BALANCES\"", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.Handled, SpotExecutionReportParser.Read(Bytes(frame), out SpotExecutionReportModel model));
        Assert.Equal("INSUFFICIENT_BALANCES", model.RejectReason);
    }

    [Theory]
    [InlineData("\"eR\":\"INSUFFICIENT_LIQUIDITY\"", SpotExpiryReason.INSUFFICIENT_LIQUIDITY, false)]
    [InlineData("\"eR\":\"NEW_REASON\"", SpotExpiryReason.UNKNOWN, true)]
    public void ReadsExpiryReasonWhenSent(string field, SpotExpiryReason expected, bool isNonStandard)
    {
        string frame = UserDataFrames.SpotExecutionReport.Replace("\"V\":\"NONE\"", "\"V\":\"NONE\"," + field, StringComparison.Ordinal);

        WsDispatchResult result = SpotExecutionReportParser.Read(Bytes(frame), out SpotExecutionReportModel model);

        Assert.Equal(isNonStandard ? WsDispatchResult.NonStandard : WsDispatchResult.Handled, result);
        Assert.Equal(expected, model.ExpiryReason);
    }

    [Fact]
    public void UnknownStatusIsDeliveredAndReported()
    {
        string frame = UserDataFrames.SpotExecutionFill.Replace("\"X\":\"PARTIALLY_FILLED\"", "\"X\":\"NEW_STATUS\"", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.NonStandard, SpotExecutionReportParser.Read(Bytes(frame), out SpotExecutionReportModel model));
        Assert.Equal(SpotOrderStatus.UNKNOWN, model.Status);
        Assert.Equal(0.5, model.FilledQuantity);
    }

    [Theory]
    [InlineData("\"i\":4293154,", "")]
    [InlineData("\"Z\":\"0.02500000\",", "")]
    [InlineData("\"r\":\"NONE\",", "")]
    [InlineData(",\"V\":\"EXPIRE_MAKER\"", "")]
    [InlineData("\"q\":\"2.00000000\"", "\"q\":\"abc\"")]
    public void ExecutionReportWithoutRequiredFieldIsFatal(string field, string replacement)
    {
        string frame = UserDataFrames.SpotExecutionFill.Replace(field, replacement, StringComparison.Ordinal);

        Assert.NotEqual(UserDataFrames.SpotExecutionFill, frame);
        Assert.Equal(WsDispatchResult.Fatal, SpotExecutionReportParser.Read(Bytes(frame), out _));
    }

    [Fact]
    public void ReadsAccountPositionAndGrowsBuffer()
    {
        SpotAccountBalance[] balances = new SpotAccountBalance[1];

        Assert.Equal(WsDispatchResult.Handled,
            SpotAccountPositionParser.Read(Bytes(UserDataFrames.SpotAccountPosition), out SpotAccountPositionHeader header, ref balances, out int count));

        Assert.Equal(1564034571105, header.EventTime);
        Assert.Equal(1564034571073, header.LastUpdateTime);
        Assert.Equal(2, count);
        Assert.Equal(Symbol.Parse("BTC"), balances[1].Asset);
        Assert.Equal(1.5, balances[1].Free);
        Assert.Equal(0.25, balances[1].Locked);
    }

    [Theory]
    [InlineData(",\"B\":[{\"a\":\"ETH\",\"f\":\"10000.000000\",\"l\":\"0.000000\"},{\"a\":\"BTC\",\"f\":\"1.500000\",\"l\":\"0.250000\"}]", "")]
    [InlineData(",\"l\":\"0.250000\"", "")]
    public void AccountPositionWithoutBalancesIsFatal(string field, string replacement)
    {
        string frame = UserDataFrames.SpotAccountPosition.Replace(field, replacement, StringComparison.Ordinal);
        SpotAccountBalance[] balances = new SpotAccountBalance[4];

        Assert.NotEqual(UserDataFrames.SpotAccountPosition, frame);
        Assert.Equal(WsDispatchResult.Fatal, SpotAccountPositionParser.Read(Bytes(frame), out _, ref balances, out _));
    }

    [Fact]
    public void ReadsBalanceUpdate()
    {
        Assert.Equal(WsDispatchResult.Handled, SpotBalanceUpdateParser.Read(Bytes(UserDataFrames.SpotBalanceUpdate), out SpotBalanceUpdateModel model));

        Assert.Equal(Symbol.Parse("BTC"), model.Asset);
        Assert.Equal(100, model.Delta);
        Assert.Equal(1573200697110, model.EventTime);
        Assert.Equal(1573200697068, model.ClearTime);
    }

    [Fact]
    public void BalanceUpdateWithoutDeltaIsFatal()
    {
        string frame = UserDataFrames.SpotBalanceUpdate.Replace("\"d\":\"100.00000000\",", "", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.Fatal, SpotBalanceUpdateParser.Read(Bytes(frame), out _));
    }

    [Fact]
    public void ReadsListStatusAndGrowsBuffer()
    {
        SpotListStatusOrder[] orders = new SpotListStatusOrder[1];

        Assert.Equal(WsDispatchResult.Handled,
            SpotListStatusParser.Read(Bytes(UserDataFrames.SpotListStatus), out SpotListStatusHeader header, ref orders, out int count));

        Assert.Equal(Symbol.Parse("ETHBTC"), header.Symbol);
        Assert.Equal(ClientOrderId.Parse("F4QN4G8DlFATFlIUQ0cjdD"), header.ListClientOrderId);
        Assert.Equal(2, header.OrderListId);
        Assert.Equal(1564035303637, header.EventTime);
        Assert.Equal(1564035303625, header.TransactionTime);
        Assert.Equal(SpotContingencyType.OCO, header.ContingencyType);
        Assert.Equal(SpotListStatusType.EXEC_STARTED, header.ListStatusType);
        Assert.Equal(SpotListOrderStatus.EXECUTING, header.ListOrderStatus);
        Assert.Null(header.RejectReason);

        Assert.Equal(2, count);
        Assert.Equal(18, orders[1].OrderId);
        Assert.Equal(ClientOrderId.Parse("bfYPSQdLoqAJeNrOr9adzq"), orders[1].ClientOrderId);
    }

    [Fact]
    public void UnknownListTypeIsDeliveredAndReported()
    {
        string frame = UserDataFrames.SpotListStatus.Replace("\"c\":\"OCO\"", "\"c\":\"OTOCO\"", StringComparison.Ordinal);
        SpotListStatusOrder[] orders = new SpotListStatusOrder[4];

        Assert.Equal(WsDispatchResult.NonStandard, SpotListStatusParser.Read(Bytes(frame), out SpotListStatusHeader header, ref orders, out int count));
        Assert.Equal(SpotContingencyType.UNKNOWN, header.ContingencyType);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ListStatusWithoutOrdersIsFatal()
    {
        int start = UserDataFrames.SpotListStatus.IndexOf(",\"O\":", StringComparison.Ordinal);
        string frame = UserDataFrames.SpotListStatus[..start] + "}";
        SpotListStatusOrder[] orders = new SpotListStatusOrder[4];

        Assert.Equal(WsDispatchResult.Fatal, SpotListStatusParser.Read(Bytes(frame), out _, ref orders, out _));
    }

    [Fact]
    public void ReadsExternalLockUpdate()
    {
        Assert.Equal(WsDispatchResult.Handled, SpotExternalLockUpdateParser.Read(Bytes(UserDataFrames.SpotExternalLockUpdate), out SpotExternalLockUpdateModel model));

        Assert.Equal(Symbol.Parse("NEO"), model.Asset);
        Assert.Equal(10, model.Delta);
        Assert.Equal(1581557507324, model.EventTime);
        Assert.Equal(1581557507268, model.TransactionTime);
    }

    [Fact]
    public void ExternalLockUpdateWithoutTimeIsFatal()
    {
        string frame = UserDataFrames.SpotExternalLockUpdate.Replace(",\"T\":1581557507268", "", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.Fatal, SpotExternalLockUpdateParser.Read(Bytes(frame), out _));
    }

    [Fact]
    public void SubscribeAnswerCarriesIdZeroIncluded() =>
        Assert.Equal(0, SubscriptionParser.Parse("""{"subscriptionId":0}"""u8).SubscriptionId);

    [Fact]
    public void UnsubscribeAnswerHasNoId() =>
        Assert.Equal(-1, SubscriptionParser.Parse("{}"u8).SubscriptionId);

    [Theory]
    [InlineData("""{"subscriptionId":"x"}""")]
    [InlineData("[]")]
    public void MalformedSubscriptionAnswerThrows(string text) =>
        Assert.Throws<JsonException>(() => SubscriptionParser.Parse(Encoding.ASCII.GetBytes(text)));

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);
}
