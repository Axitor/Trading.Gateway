using System.Text;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.OrderRules;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Tests.Binance.Rules;

/// <summary>Symbol filters: a price goes to the nearest tick, a quantity down to its step, and a broken rule is caught before the rate limiter.</summary>
public sealed class OrderRulesTests
{
    [Theory]
    [InlineData(60000.123, 0.1, 0.1, "60000.1")]
    [InlineData(0.123456, 0.0001, 0.0001, "0.1235")]
    [InlineData(3456.789, 0.01, 0.01, "3456.79")]
    [InlineData(99.96, 0.5, 0.5, "100")]
    [InlineData(1.2345, 0, 0, "1.2345")]
    public void PriceGoesToNearestTick(double price, double tickSize, double minPrice, string expected)
    {
        SymbolFilters filters = new() { TickSize = tickSize, MinPrice = minPrice };

        Assert.Equal(expected, Wire(BinanceOrderRules.RoundPrice(in filters, price)));
    }

    [Theory]
    [InlineData(0.0129, 0.001, 0.001, 0, 0, false, "0.012")]
    [InlineData(0.3, 0.1, 0.1, 0, 0, false, "0.3")]
    [InlineData(7.9, 0.001, 0.001, 1, 1, true, "7")]
    [InlineData(7.9, 0.001, 0.001, 1, 1, false, "7.9")]
    [InlineData(7.95, 0.1, 0.1, 0, 0, true, "7.9")]
    [InlineData(1.23456789, 0, 0, 0, 0, false, "1.23456789")]
    public void QuantityGoesDownToStep(double quantity, double stepSize, double minQuantity, double marketStepSize, double marketMinQuantity, bool isMarket, string expected)
    {
        SymbolFilters filters = new()
        {
            StepSize = stepSize,
            MinQuantity = minQuantity,
            MarketStepSize = marketStepSize,
            MarketMinQuantity = marketMinQuantity,
        };

        Assert.Equal(expected, Wire(BinanceOrderRules.RoundQuantity(in filters, quantity, isMarket)));
    }

    [Theory]
    [InlineData(0.05, 0.1, 1000, OrderRuleViolation.PriceBelowMin)]
    [InlineData(1000.5, 0.1, 1000, OrderRuleViolation.PriceAboveMax)]
    [InlineData(0.1, 0.1, 1000, OrderRuleViolation.None)]
    [InlineData(1000, 0.1, 1000, OrderRuleViolation.None)]
    [InlineData(0.30000000000000004, 0.1, 0.3, OrderRuleViolation.None)]
    [InlineData(5, 0, 0, OrderRuleViolation.None)]
    public void ChecksPriceLimits(double price, double minPrice, double maxPrice, OrderRuleViolation expected)
    {
        SymbolFilters filters = new() { MinPrice = minPrice, MaxPrice = maxPrice };

        Assert.Equal(expected, BinanceOrderRules.CheckPrice(in filters, price));
    }

    [Theory]
    [InlineData(0.0005, 0.001, 1000, 0, 0, false, OrderRuleViolation.QuantityBelowMin)]
    [InlineData(1001, 0.001, 1000, 0, 0, false, OrderRuleViolation.QuantityAboveMax)]
    [InlineData(150, 0.001, 1000, 1, 120, true, OrderRuleViolation.QuantityAboveMax)]
    [InlineData(0.5, 0.001, 1000, 1, 120, true, OrderRuleViolation.QuantityBelowMin)]
    [InlineData(150, 0.001, 1000, 0, 0, true, OrderRuleViolation.None)]
    [InlineData(0.30000000000000004, 0.3, 1000, 0, 0, false, OrderRuleViolation.None)]
    public void ChecksQuantityLimitsOfOrderKind(double quantity, double minQuantity, double maxQuantity, double marketMinQuantity, double marketMaxQuantity, bool isMarket, OrderRuleViolation expected)
    {
        SymbolFilters filters = new()
        {
            MinQuantity = minQuantity,
            MaxQuantity = maxQuantity,
            MarketMinQuantity = marketMinQuantity,
            MarketMaxQuantity = marketMaxQuantity,
        };

        Assert.Equal(expected, BinanceOrderRules.CheckQuantity(in filters, quantity, isMarket));
    }

    [Theory]
    [InlineData(100, 0.0499, 5, OrderRuleViolation.NotionalBelowMin)]
    [InlineData(100, 0.05, 5, OrderRuleViolation.None)]
    [InlineData(0.1, 50, 5, OrderRuleViolation.None)]
    public void ChecksNotional(double price, double quantity, double minNotional, OrderRuleViolation expected)
    {
        SymbolFilters filters = new() { MinNotional = minNotional };

        Assert.Equal(expected, BinanceOrderRules.CheckNotional(in filters, price, quantity));
    }

    [Theory]
    [InlineData(OrderType.MARKET, true)]
    [InlineData(OrderType.STOP_MARKET, true)]
    [InlineData(OrderType.TAKE_PROFIT_MARKET, true)]
    [InlineData(OrderType.TRAILING_STOP_MARKET, true)]
    [InlineData(OrderType.LIMIT, false)]
    [InlineData(OrderType.STOP, false)]
    public void MarketTypesFollowMarketLotRules(OrderType type, bool expected) =>
        Assert.Equal(expected, BinanceOrderRules.IsMarketType(type));

    [Fact]
    public void OrderIsRoundedBeforeSending()
    {
        NewOrderRequest request = Limit(price: 60000.123, quantity: 0.0129);

        Assert.Equal(OrderRuleViolation.None, BinanceOrderPreflight.Prepare(ref request));
        Assert.Equal("60000.1", Wire(request.Price));
        Assert.Equal("0.012", Wire(request.Quantity));
    }

    [Fact]
    public void QuantityRoundedBelowMinIsRejected()
    {
        NewOrderRequest request = Limit(price: 60000, quantity: 0.0009);

        Assert.Equal(OrderRuleViolation.QuantityBelowMin, BinanceOrderPreflight.Prepare(ref request));
    }

    [Fact]
    public void NotionalIsSkippedForReduceOnly()
    {
        NewOrderRequest opening = Limit(price: 100, quantity: 0.01);
        NewOrderRequest reducing = Limit(price: 100, quantity: 0.01);
        reducing.IsReduceOnly = true;

        Assert.Equal(OrderRuleViolation.NotionalBelowMin, BinanceOrderPreflight.Prepare(ref opening));
        Assert.Equal(OrderRuleViolation.None, BinanceOrderPreflight.Prepare(ref reducing));
    }

    [Fact]
    public void MarketOrderKeepsZeroPriceAndSkipsNotional()
    {
        NewOrderRequest request = new()
        {
            Symbol = Symbol.Parse("BTCUSDT"),
            ClientOrderId = ClientOrderId.Parse("m-1"),
            Filters = Filters(),
            Side = OrderSide.BUY,
            Type = OrderType.MARKET,
            Quantity = 0.001,
        };

        // Zero means no price is sent: it must not be rounded up to MinPrice.
        Assert.Equal(OrderRuleViolation.None, BinanceOrderPreflight.Prepare(ref request));
        Assert.Equal(0, request.Price);
    }

    [Fact]
    public void MarketOrderUsesMarketLotSize()
    {
        SymbolFilters filters = Filters();
        filters.MarketStepSize = 1;
        filters.MarketMinQuantity = 1;
        filters.MarketMaxQuantity = 120;

        NewOrderRequest request = new()
        {
            Symbol = Symbol.Parse("BTCUSDT"),
            ClientOrderId = ClientOrderId.Parse("m-2"),
            Filters = filters,
            Side = OrderSide.SELL,
            Type = OrderType.MARKET,
            Quantity = 7.9,
        };

        Assert.Equal(OrderRuleViolation.None, BinanceOrderPreflight.Prepare(ref request));
        Assert.Equal("7", Wire(request.Quantity));
    }

    [Fact]
    public void ClosePositionStopChecksNoQuantity()
    {
        NewAlgoOrderRequest request = Stop(triggerPrice: 55000.07);

        Assert.Equal(OrderRuleViolation.None, BinanceOrderPreflight.Prepare(ref request));
        Assert.Equal("55000.1", Wire(request.TriggerPrice));
        Assert.Equal(0, request.Quantity);
    }

    [Fact]
    public void TriggerBelowMinPriceIsRejected()
    {
        NewAlgoOrderRequest request = Stop(triggerPrice: 0.04);

        Assert.Equal(OrderRuleViolation.PriceBelowMin, BinanceOrderPreflight.Prepare(ref request));
    }

    [Fact]
    public void ModifyChecksNotionalUnlessReduceOnly()
    {
        SymbolFilters filters = Filters();
        double quantity = 0.01;
        double price = 100.04;

        Assert.Equal(OrderRuleViolation.NotionalBelowMin, BinanceOrderPreflight.PrepareModify(in filters, ref quantity, ref price, isReduceOnly: false));

        quantity = 0.01;
        price = 100.04;

        Assert.Equal(OrderRuleViolation.None, BinanceOrderPreflight.PrepareModify(in filters, ref quantity, ref price, isReduceOnly: true));
        Assert.Equal("100", Wire(price));
    }

    /// <summary>Filters of a BTCUSDT-like symbol.</summary>
    private static SymbolFilters Filters() => new()
    {
        TickSize = 0.1,
        MinPrice = 0.1,
        MaxPrice = 1_000_000,
        StepSize = 0.001,
        MinQuantity = 0.001,
        MaxQuantity = 1000,
        MinNotional = 5,
    };

    private static NewOrderRequest Limit(double price, double quantity) => new()
    {
        Symbol = Symbol.Parse("BTCUSDT"),
        ClientOrderId = ClientOrderId.Parse("l-1"),
        Filters = Filters(),
        Side = OrderSide.BUY,
        Type = OrderType.LIMIT,
        TimeInForce = TimeInForce.GTC,
        Price = price,
        Quantity = quantity,
    };

    private static NewAlgoOrderRequest Stop(double triggerPrice) => new()
    {
        Symbol = Symbol.Parse("BTCUSDT"),
        ClientAlgoId = ClientOrderId.Parse("s-1"),
        Filters = Filters(),
        Side = OrderSide.SELL,
        Type = OrderType.STOP_MARKET,
        TriggerPrice = triggerPrice,
        IsClosePosition = true,
    };

    /// <summary>The value as it goes out on the wire.</summary>
    private static string Wire(double value)
    {
        Span<byte> text = stackalloc byte[64];
        Assert.True(PlainDecimalFormatter.TryFormat(value, text, out int written));
        return Encoding.ASCII.GetString(text[..written]);
    }
}
