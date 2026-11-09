using Axitor.Trading.Gateway.Binance.Models.Common.Enums;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Extensions;

/// <summary>Exchange spelling of order enums as UTF-8 bytes. UNKNOWN writes nothing, and the exchange rejects the request.</summary>
internal static class BinanceOrderEnumExtensions
{
    public static ReadOnlySpan<byte> ToUtf8Bytes(this OrderSide side) =>
        side switch
        {
            OrderSide.BUY => "BUY"u8,
            OrderSide.SELL => "SELL"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this OrderType type) =>
        type switch
        {
            OrderType.LIMIT => "LIMIT"u8,
            OrderType.MARKET => "MARKET"u8,
            OrderType.STOP => "STOP"u8,
            OrderType.STOP_MARKET => "STOP_MARKET"u8,
            OrderType.TAKE_PROFIT => "TAKE_PROFIT"u8,
            OrderType.TAKE_PROFIT_MARKET => "TAKE_PROFIT_MARKET"u8,
            OrderType.TRAILING_STOP_MARKET => "TRAILING_STOP_MARKET"u8,
            OrderType.LIQUIDATION => "LIQUIDATION"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this TimeInForce timeInForce) =>
        timeInForce switch
        {
            TimeInForce.GTC => "GTC"u8,
            TimeInForce.IOC => "IOC"u8,
            TimeInForce.FOK => "FOK"u8,
            TimeInForce.GTX => "GTX"u8,
            TimeInForce.GTD => "GTD"u8,
            TimeInForce.RPI => "RPI"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this PositionSide side) =>
        side switch
        {
            PositionSide.BOTH => "BOTH"u8,
            PositionSide.LONG => "LONG"u8,
            PositionSide.SHORT => "SHORT"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this WorkingType workingType) =>
        workingType switch
        {
            WorkingType.MARK_PRICE => "MARK_PRICE"u8,
            WorkingType.CONTRACT_PRICE => "CONTRACT_PRICE"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this PriceMatch priceMatch) =>
        priceMatch switch
        {
            PriceMatch.NONE => "NONE"u8,
            PriceMatch.OPPONENT => "OPPONENT"u8,
            PriceMatch.OPPONENT_5 => "OPPONENT_5"u8,
            PriceMatch.OPPONENT_10 => "OPPONENT_10"u8,
            PriceMatch.OPPONENT_20 => "OPPONENT_20"u8,
            PriceMatch.QUEUE => "QUEUE"u8,
            PriceMatch.QUEUE_5 => "QUEUE_5"u8,
            PriceMatch.QUEUE_10 => "QUEUE_10"u8,
            PriceMatch.QUEUE_20 => "QUEUE_20"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this SelfTradePreventionMode mode) =>
        mode switch
        {
            SelfTradePreventionMode.NONE => "NONE"u8,
            SelfTradePreventionMode.EXPIRE_TAKER => "EXPIRE_TAKER"u8,
            SelfTradePreventionMode.EXPIRE_MAKER => "EXPIRE_MAKER"u8,
            SelfTradePreventionMode.EXPIRE_BOTH => "EXPIRE_BOTH"u8,
            _ => ""u8,
        };

    public static ReadOnlySpan<byte> ToUtf8Bytes(this OrderResponseType responseType) =>
        responseType switch
        {
            OrderResponseType.ACK => "ACK"u8,
            OrderResponseType.RESULT => "RESULT"u8,
            _ => ""u8,
        };

    // The exchange takes CROSSED, while ACCOUNT_UPDATE reports cross: MarginType.CROSS covers both.
    public static ReadOnlySpan<byte> ToUtf8Bytes(this MarginType marginType) =>
        marginType switch
        {
            MarginType.ISOLATED => "ISOLATED"u8,
            MarginType.CROSS => "CROSSED"u8,
            _ => ""u8,
        };
}
