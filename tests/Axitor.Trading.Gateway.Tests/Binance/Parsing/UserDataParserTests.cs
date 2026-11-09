using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.UserData;
using Axitor.Trading.Gateway.Binance.Parsing.UserData;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;
using Axitor.Trading.Gateway.Tests.Frames;

namespace Axitor.Trading.Gateway.Tests.Binance.Parsing;

/// <summary>Futures user data: a lost fill leaves the position wrong, so an unreadable required field stops the session.</summary>
public sealed class UserDataParserTests
{
    [Fact]
    public void ReadsOrderFill()
    {
        Assert.Equal(WsDispatchResult.Handled, OrderUpdateParser.Read(Bytes(UserDataFrames.OrderTradeUpdate), out OrderUpdateModel model));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(ClientOrderId.Parse("my-order-1"), model.ClientOrderId);
        Assert.Equal(4000000001, model.OrderId);
        Assert.Equal(555, model.TradeId);
        Assert.Equal(1700000000123, model.EventTime);
        Assert.Equal(1700000000120, model.TransactionTime);
        Assert.Equal(1700000000120, model.TradeTime);
        Assert.Equal(0.01, model.Quantity);
        Assert.Equal(60000, model.Price);
        Assert.Equal(60000, model.AveragePrice);
        Assert.Equal(0.004, model.LastFilledQuantity);
        Assert.Equal(0.004, model.FilledQuantity);
        Assert.Equal(60000, model.LastFilledPrice);
        Assert.Equal(Symbol.Parse("USDT"), model.CommissionAsset);
        Assert.Equal(0.096, model.Commission);
        Assert.Equal(360, model.BidsNotional);
        Assert.Equal(1.5, model.RealizedProfit);
        Assert.Equal(OrderSide.BUY, model.Side);
        Assert.Equal(OrderType.LIMIT, model.Type);
        Assert.Equal(OrderType.LIMIT, model.OriginalType);
        Assert.Equal(TimeInForce.GTC, model.TimeInForce);
        Assert.Equal(ExecutionType.TRADE, model.ExecutionType);
        Assert.Equal(OrderStatus.PARTIALLY_FILLED, model.Status);
        Assert.Equal(WorkingType.CONTRACT_PRICE, model.WorkingType);
        Assert.Equal(PositionSide.BOTH, model.PositionSide);
        Assert.Equal(ExpiryReason.NONE, model.ExpiryReason);
        Assert.True(model.IsMaker);
        Assert.False(model.IsReduceOnly);
    }

    [Fact]
    public void UnknownStatusIsDeliveredAndReported()
    {
        string frame = UserDataFrames.OrderTradeUpdate.Replace("\"X\":\"PARTIALLY_FILLED\"", "\"X\":\"NEW_STATUS\"", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.NonStandard, OrderUpdateParser.Read(Bytes(frame), out OrderUpdateModel model));
        Assert.Equal(OrderStatus.UNKNOWN, model.Status);
        Assert.Equal(4000000001, model.OrderId);
        Assert.Equal(0.004, model.FilledQuantity);
    }

    [Theory]
    [InlineData("\"i\":4000000001,", "")]
    [InlineData("\"z\":\"0.004\",", "")]
    [InlineData("\"E\":1700000000123,", "")]
    [InlineData("\"T\":1700000000120,\"o\"", "\"o\"")]
    [InlineData("\"q\":\"0.010\"", "\"q\":\"abc\"")]
    [InlineData("\"ps\":\"BOTH\"", "\"ps\":\"MIDDLE\"")]
    public void OrderUpdateWithoutRequiredFieldIsFatal(string field, string replacement)
    {
        string frame = UserDataFrames.OrderTradeUpdate.Replace(field, replacement, StringComparison.Ordinal);

        Assert.NotEqual(UserDataFrames.OrderTradeUpdate, frame);
        Assert.Equal(WsDispatchResult.Fatal, OrderUpdateParser.Read(Bytes(frame), out _));
    }

    [Theory]
    [InlineData("10", ExpiryReason.FOK_UNFILLED, false)]
    [InlineData("11", ExpiryReason.POST_ONLY_REJECTED, false)]
    [InlineData("12", (ExpiryReason)12, true)]
    [InlineData("300", ExpiryReason.NONE, true)]
    public void ExpiryReasonKeepsUnknownCode(string code, ExpiryReason expected, bool isNonStandard)
    {
        string frame = UserDataFrames.OrderTradeUpdate.Replace("\"er\":\"0\"", "\"er\":\"" + code + "\"", StringComparison.Ordinal);

        WsDispatchResult result = OrderUpdateParser.Read(Bytes(frame), out OrderUpdateModel model);

        Assert.Equal(isNonStandard ? WsDispatchResult.NonStandard : WsDispatchResult.Handled, result);
        Assert.Equal(expected, model.ExpiryReason);
    }

    [Fact]
    public void ReadsAccountUpdateAndGrowsBuffers()
    {
        AccountUpdateBalance[] balances = new AccountUpdateBalance[1];
        AccountUpdatePosition[] positions = new AccountUpdatePosition[1];

        WsDispatchResult result = AccountUpdateParser.Read(
            Bytes(UserDataFrames.AccountUpdate), out AccountUpdateHeader header, ref balances, out int balanceCount, ref positions, out int positionCount);

        Assert.Equal(WsDispatchResult.Handled, result);
        Assert.Equal(1564745798939, header.EventTime);
        Assert.Equal(1564745798938, header.TransactionTime);
        Assert.Equal(AccountUpdateReason.ORDER, header.Reason);

        Assert.Equal(2, balanceCount);
        Assert.Equal(Symbol.Parse("BUSD"), balances[1].Asset);
        Assert.Equal(1, balances[1].WalletBalance);
        Assert.Equal(-49.12345678, balances[1].BalanceChange);

        Assert.Equal(2, positionCount);
        AccountUpdatePosition position = positions[1];
        Assert.Equal(Symbol.Parse("BTCUSDT"), position.Symbol);
        Assert.Equal(20, position.Quantity);
        Assert.Equal(6563.665, position.EntryPrice);
        Assert.Equal(6563.6, position.BreakevenPrice);
        Assert.Equal(2850.212, position.UnrealizedProfit);
        Assert.Equal(13200.70726908, position.IsolatedWallet);
        Assert.Equal(MarginType.ISOLATED, position.MarginType);
        Assert.Equal(PositionSide.LONG, position.Side);
    }

    [Theory]
    [InlineData("\"iw\":\"13200.70726908\",", "")]
    [InlineData("\"wb\":\"1.00000000\",", "")]
    [InlineData("\"m\":\"ORDER\",", "")]
    public void AccountUpdateWithoutRequiredFieldIsFatal(string field, string replacement)
    {
        string frame = UserDataFrames.AccountUpdate.Replace(field, replacement, StringComparison.Ordinal);
        AccountUpdateBalance[] balances = new AccountUpdateBalance[4];
        AccountUpdatePosition[] positions = new AccountUpdatePosition[4];

        Assert.NotEqual(UserDataFrames.AccountUpdate, frame);
        Assert.Equal(WsDispatchResult.Fatal, AccountUpdateParser.Read(Bytes(frame), out _, ref balances, out _, ref positions, out _));
    }

    [Fact]
    public void ReadsTradeLite()
    {
        Assert.Equal(WsDispatchResult.Handled, TradeLiteParser.Read(Bytes(UserDataFrames.TradeLite), out TradeLiteModel model));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(ClientOrderId.Parse("z8hcUoOsqEdKMeKPSABslD"), model.ClientOrderId);
        Assert.Equal(8886774, model.OrderId);
        Assert.Equal(109100866, model.TradeId);
        Assert.Equal(1721895408092, model.EventTime);
        Assert.Equal(1721895408214, model.TransactionTime);
        Assert.Equal(0.001, model.Quantity);
        Assert.Equal(0, model.Price);
        Assert.Equal(0.04, model.LastFilledQuantity);
        Assert.Equal(64089.2, model.LastFilledPrice);
        Assert.Equal(OrderSide.BUY, model.Side);
        Assert.False(model.IsMaker);
    }

    [Fact]
    public void TradeLiteWithoutFieldIsFatal()
    {
        string frame = UserDataFrames.TradeLite.Replace("\"t\":109100866,", "", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.Fatal, TradeLiteParser.Read(Bytes(frame), out _));
    }

    [Fact]
    public void ReadsAlgoUpdateBeforeTrigger()
    {
        // ai "" and act "0" mean not triggered yet, not malformed.
        Assert.Equal(WsDispatchResult.Handled, AlgoUpdateParser.Read(Bytes(UserDataFrames.AlgoUpdate), out AlgoUpdateModel model));

        Assert.Equal(Symbol.Parse("BNBUSDT"), model.Symbol);
        Assert.Equal(ClientOrderId.Parse("Q5xaq5EGKgXXa0fD7fs0Ip"), model.ClientAlgoId);
        Assert.Equal(2148719, model.AlgoId);
        Assert.Equal(0, model.OrderId);
        Assert.Equal(OrderType.UNKNOWN, model.ActualType);
        Assert.Equal(1750515742303, model.EventTime);
        Assert.Equal(1750515742297, model.TransactionTime);
        Assert.Equal(0.01, model.Quantity);
        Assert.Equal(750, model.Price);
        Assert.Equal(750, model.TriggerPrice);
        Assert.Equal(AlgoStatus.CANCELED, model.Status);
        Assert.Equal(AlgoType.CONDITIONAL, model.AlgoType);
        Assert.Equal(OrderType.TAKE_PROFIT, model.Type);
        Assert.Equal(OrderSide.SELL, model.Side);
        Assert.Equal(PositionSide.BOTH, model.PositionSide);
        Assert.Equal(TimeInForce.GTC, model.TimeInForce);
        Assert.Equal(WorkingType.CONTRACT_PRICE, model.WorkingType);
        Assert.Equal("Reduce Only reject", model.RejectReason);
    }

    [Fact]
    public void AlgoUpdateWithoutIdIsFatal()
    {
        string frame = UserDataFrames.AlgoUpdate.Replace("\"aid\":2148719,", "", StringComparison.Ordinal);

        Assert.Equal(WsDispatchResult.Fatal, AlgoUpdateParser.Read(Bytes(frame), out _));
    }

    [Fact]
    public void ReadsLeverageChange()
    {
        Assert.Equal(WsDispatchResult.Handled, AccountConfigUpdateParser.Read(Bytes(UserDataFrames.LeverageUpdate), out AccountConfigUpdateModel model));

        Assert.Equal(Symbol.Parse("BTCUSDT"), model.Symbol);
        Assert.Equal(25, model.Leverage);
        Assert.False(model.IsMultiAssetsModeChange);
    }

    [Fact]
    public void ReadsMultiAssetsChange()
    {
        Assert.Equal(WsDispatchResult.Handled, AccountConfigUpdateParser.Read(Bytes(UserDataFrames.MultiAssetsUpdate), out AccountConfigUpdateModel model));

        Assert.True(model.IsMultiAssetsModeChange);
        Assert.True(model.IsMultiAssetsMode);
        Assert.True(model.Symbol.IsEmpty);
    }

    [Fact]
    public void UnknownConfigChangeIsDeliveredAndReported()
    {
        // The bot still learns the account changed.
        byte[] frame = """{"e":"ACCOUNT_CONFIG_UPDATE","E":1611646737479,"T":1611646737476,"xx":{"y":1}}"""u8.ToArray();

        Assert.Equal(WsDispatchResult.NonStandard, AccountConfigUpdateParser.Read(frame, out AccountConfigUpdateModel model));
        Assert.Equal(1611646737479, model.EventTime);
        Assert.False(model.IsMultiAssetsModeChange);
        Assert.Equal(0, model.Leverage);
    }

    [Fact]
    public void LeverageChangeWithoutLeverageIsFatal()
    {
        byte[] frame = """{"e":"ACCOUNT_CONFIG_UPDATE","E":1611646737479,"T":1611646737476,"ac":{"s":"BTCUSDT"}}"""u8.ToArray();

        Assert.Equal(WsDispatchResult.Fatal, AccountConfigUpdateParser.Read(frame, out _));
    }

    [Fact]
    public void ReadsMarginCall()
    {
        MarginCallPosition[] positions = new MarginCallPosition[1];

        Assert.Equal(WsDispatchResult.Handled, MarginCallParser.Read(Bytes(UserDataFrames.MarginCall), out MarginCallHeader header, ref positions, out int count));

        Assert.Equal(1587727187525, header.EventTime);
        Assert.Equal(3.16812045, header.CrossWalletBalance);

        Assert.Equal(1, count);
        MarginCallPosition position = positions[0];
        Assert.Equal(Symbol.Parse("ETHUSDT"), position.Symbol);
        Assert.Equal(PositionSide.LONG, position.Side);
        Assert.Equal(1.327, position.Quantity);
        Assert.Equal(MarginType.CROSS, position.MarginType);
        Assert.Equal(187.17127, position.MarkPrice);
        Assert.Equal(-1.166074, position.UnrealizedProfit);
        Assert.Equal(1.614445, position.MaintenanceMargin);
    }

    [Fact]
    public void MarginCallPositionWithoutFieldIsFatal()
    {
        string frame = UserDataFrames.MarginCall.Replace("\"mm\":\"1.614445\"", "\"x\":0", StringComparison.Ordinal);
        MarginCallPosition[] positions = new MarginCallPosition[1];

        Assert.Equal(WsDispatchResult.Fatal, MarginCallParser.Read(Bytes(frame), out _, ref positions, out _));
    }

    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);
}
