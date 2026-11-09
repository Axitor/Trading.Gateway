using System.Buffers.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Parsing;

namespace Axitor.Trading.Gateway.Binance.Parsing.Readers;

/// <summary>Reads typed values from the token a Utf8JsonReader sits on. False when the value is malformed.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: An unknown enum value or a client order id that does not fit is not malformed: it reads as UNKNOWN or Empty
/// and sets nonStandard, so the event still reaches the handler and the miss reaches telemetry.
/// </para>
/// <para>
/// DESIGN NOTE: Numbers go through the same decimal parser as the streams, so one price text reads to one double everywhere.
/// </para>
/// </remarks>
internal static class BinanceJsonFieldReader
{
    /// <summary>Reads a number, quoted as prices and quantities come, or bare.</summary>
    public static bool TryReadDouble(ref Utf8JsonReader reader, out double value)
    {
        if (reader.TokenType == JsonTokenType.Number || (reader.TokenType == JsonTokenType.String && !reader.ValueIsEscaped))
        {
            ReadOnlySpan<byte> text = reader.ValueSpan;

            if (FastDecimalParser.TryParse(text, out value, out int consumed) && consumed == text.Length)
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Reads an integer, bare as times and ids come, or quoted.</summary>
    public static bool TryReadInt64(ref Utf8JsonReader reader, out long value)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out value))
        {
            return true;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            ReadOnlySpan<byte> text = reader.ValueSpan;

            if (!reader.ValueIsEscaped && Utf8Parser.TryParse(text, out value, out int consumed) && consumed == text.Length)
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Reads an integer that fits in 32 bits, bare or quoted.</summary>
    public static bool TryReadInt32(ref Utf8JsonReader reader, out int value)
    {
        if (TryReadInt64(ref reader, out long wide) && int.MinValue <= wide && wide <= int.MaxValue)
        {
            value = (int)wide;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Reads an integer, bare or quoted. An empty string reads as zero: the id is not assigned yet.</summary>
    public static bool TryReadInt64OrUnset(ref Utf8JsonReader reader, out long value)
    {
        if (reader.TokenType == JsonTokenType.String && reader.ValueSpan.IsEmpty)
        {
            value = 0;
            return true;
        }

        return TryReadInt64(ref reader, out value);
    }

    /// <summary>Reads true or false.</summary>
    public static bool TryReadBoolean(ref Utf8JsonReader reader, out bool value)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            value = true;
            return true;
        }

        value = false;
        return reader.TokenType == JsonTokenType.False;
    }

    /// <summary>Reads a symbol or an asset name.</summary>
    public static bool TryReadSymbol(ref Utf8JsonReader reader, out Symbol value)
    {
        if (reader.TokenType == JsonTokenType.String && !reader.ValueIsEscaped && Symbol.TryParse(reader.ValueSpan, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Reads a client order id. One that does not fit reads as Empty and sets nonStandard.</summary>
    public static bool TryReadClientOrderId(ref Utf8JsonReader reader, out ClientOrderId value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = default;
            return false;
        }

        if (reader.ValueIsEscaped || !ClientOrderId.TryParse(reader.ValueSpan, out value))
        {
            value = ClientOrderId.Empty;
            nonStandard = true;
        }

        return true;
    }

    /// <summary>Cold path. Reads text as a string. An empty one reads as null without allocating.</summary>
    public static bool TryReadText(ref Utf8JsonReader reader, out string? value)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = null;
            return false;
        }

        value = reader.ValueSpan.IsEmpty ? null : reader.GetString();
        return true;
    }

    /// <summary>Reads a reject reason. NONE reads as null without allocating; a real reason allocates a string.</summary>
    public static bool TryReadRejectReason(ref Utf8JsonReader reader, out string? value)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = null;
            return false;
        }

        value = reader.ValueTextEquals("NONE"u8) ? null : reader.GetString();
        return true;
    }

    /// <summary>Reads a position side. It has no UNKNOWN, so an unknown side is malformed.</summary>
    public static bool TryReadPositionSide(ref Utf8JsonReader reader, out PositionSide value)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            ReadOnlySpan<byte> text = reader.ValueSpan;

            if (text.SequenceEqual("BOTH"u8))
            {
                value = PositionSide.BOTH;
                return true;
            }

            if (text.SequenceEqual("LONG"u8))
            {
                value = PositionSide.LONG;
                return true;
            }

            if (text.SequenceEqual("SHORT"u8))
            {
                value = PositionSide.SHORT;
                return true;
            }
        }

        value = PositionSide.BOTH;
        return false;
    }

    /// <summary>Reads an expiry reason code. A code this version does not know is kept as a number and sets nonStandard.</summary>
    public static bool TryReadExpiryReason(ref Utf8JsonReader reader, out ExpiryReason value, ref bool nonStandard)
    {
        if (!TryReadInt64(ref reader, out long code))
        {
            value = ExpiryReason.NONE;
            return false;
        }

        if ((ulong)code <= (ulong)ExpiryReason.POST_ONLY_REJECTED)
        {
            value = (ExpiryReason)code;
            return true;
        }

        nonStandard = true;
        value = (ulong)code <= byte.MaxValue ? (ExpiryReason)code : ExpiryReason.NONE;
        return true;
    }

    /// <summary>Reads an order side. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadOrderSide(ref Utf8JsonReader reader, out OrderSide value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = OrderSide.UNKNOWN;
            return false;
        }

        value = ToOrderSide(reader.ValueSpan);
        nonStandard |= value == OrderSide.UNKNOWN;
        return true;
    }

    /// <summary>Reads an order type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadOrderType(ref Utf8JsonReader reader, out OrderType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = OrderType.UNKNOWN;
            return false;
        }

        value = ToOrderType(reader.ValueSpan);
        nonStandard |= value == OrderType.UNKNOWN;
        return true;
    }

    /// <summary>Reads an order type that may be unset, sent as "0" or empty: that reads as UNKNOWN without setting nonStandard.</summary>
    public static bool TryReadOrderTypeOrUnset(ref Utf8JsonReader reader, out OrderType value, ref bool nonStandard)
    {
        if ((reader.TokenType is JsonTokenType.String or JsonTokenType.Number)
            && (reader.ValueSpan.IsEmpty || reader.ValueSpan.SequenceEqual("0"u8)))
        {
            value = OrderType.UNKNOWN;
            return true;
        }

        return TryReadOrderType(ref reader, out value, ref nonStandard);
    }

    /// <summary>Reads an execution type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadExecutionType(ref Utf8JsonReader reader, out ExecutionType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = ExecutionType.UNKNOWN;
            return false;
        }

        value = ToExecutionType(reader.ValueSpan);
        nonStandard |= value == ExecutionType.UNKNOWN;
        return true;
    }

    /// <summary>Reads an order status. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadOrderStatus(ref Utf8JsonReader reader, out OrderStatus value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = OrderStatus.UNKNOWN;
            return false;
        }

        value = ToOrderStatus(reader.ValueSpan);
        nonStandard |= value == OrderStatus.UNKNOWN;
        return true;
    }

    /// <summary>Reads a time in force. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadTimeInForce(ref Utf8JsonReader reader, out TimeInForce value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = TimeInForce.UNKNOWN;
            return false;
        }

        value = ToTimeInForce(reader.ValueSpan);
        nonStandard |= value == TimeInForce.UNKNOWN;
        return true;
    }

    /// <summary>Reads a working type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadWorkingType(ref Utf8JsonReader reader, out WorkingType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = WorkingType.UNKNOWN;
            return false;
        }

        value = ToWorkingType(reader.ValueSpan);
        nonStandard |= value == WorkingType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a margin type, spelled cross or CROSSED. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadMarginType(ref Utf8JsonReader reader, out MarginType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = MarginType.UNKNOWN;
            return false;
        }

        value = ToMarginType(reader.ValueSpan);
        nonStandard |= value == MarginType.UNKNOWN;
        return true;
    }

    /// <summary>Reads an account update reason. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadAccountUpdateReason(ref Utf8JsonReader reader, out AccountUpdateReason value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = AccountUpdateReason.UNKNOWN;
            return false;
        }

        value = ToAccountUpdateReason(reader.ValueSpan);
        nonStandard |= value == AccountUpdateReason.UNKNOWN;
        return true;
    }

    /// <summary>Reads an algo status. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadAlgoStatus(ref Utf8JsonReader reader, out AlgoStatus value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = AlgoStatus.UNKNOWN;
            return false;
        }

        value = ToAlgoStatus(reader.ValueSpan);
        nonStandard |= value == AlgoStatus.UNKNOWN;
        return true;
    }

    /// <summary>Reads an algo type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadAlgoType(ref Utf8JsonReader reader, out AlgoType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = AlgoType.UNKNOWN;
            return false;
        }

        value = ToAlgoType(reader.ValueSpan);
        nonStandard |= value == AlgoType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a price match mode. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadPriceMatch(ref Utf8JsonReader reader, out PriceMatch value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = PriceMatch.UNKNOWN;
            return false;
        }

        value = ToPriceMatch(reader.ValueSpan);
        nonStandard |= value == PriceMatch.UNKNOWN;
        return true;
    }

    /// <summary>Reads a self-trade prevention mode. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSelfTradePreventionMode(ref Utf8JsonReader reader, out SelfTradePreventionMode value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SelfTradePreventionMode.UNKNOWN;
            return false;
        }

        value = ToSelfTradePreventionMode(reader.ValueSpan);
        nonStandard |= value == SelfTradePreventionMode.UNKNOWN;
        return true;
    }

    /// <summary>Reads a symbol status. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSymbolStatus(ref Utf8JsonReader reader, out SymbolStatus value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SymbolStatus.UNKNOWN;
            return false;
        }

        value = ToSymbolStatus(reader.ValueSpan);
        nonStandard |= value == SymbolStatus.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot order type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotOrderType(ref Utf8JsonReader reader, out SpotOrderType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotOrderType.UNKNOWN;
            return false;
        }

        value = ToSpotOrderType(reader.ValueSpan);
        nonStandard |= value == SpotOrderType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot order status. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotOrderStatus(ref Utf8JsonReader reader, out SpotOrderStatus value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotOrderStatus.UNKNOWN;
            return false;
        }

        value = ToSpotOrderStatus(reader.ValueSpan);
        nonStandard |= value == SpotOrderStatus.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot execution type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotExecutionType(ref Utf8JsonReader reader, out SpotExecutionType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotExecutionType.UNKNOWN;
            return false;
        }

        value = ToSpotExecutionType(reader.ValueSpan);
        nonStandard |= value == SpotExecutionType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot self-trade prevention mode. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotSelfTradePreventionMode(ref Utf8JsonReader reader, out SpotSelfTradePreventionMode value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotSelfTradePreventionMode.UNKNOWN;
            return false;
        }

        value = ToSpotSelfTradePreventionMode(reader.ValueSpan);
        nonStandard |= value == SpotSelfTradePreventionMode.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot expiry reason. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotExpiryReason(ref Utf8JsonReader reader, out SpotExpiryReason value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotExpiryReason.UNKNOWN;
            return false;
        }

        value = ToSpotExpiryReason(reader.ValueSpan);
        nonStandard |= value == SpotExpiryReason.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot contingency type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotContingencyType(ref Utf8JsonReader reader, out SpotContingencyType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotContingencyType.UNKNOWN;
            return false;
        }

        value = ToSpotContingencyType(reader.ValueSpan);
        nonStandard |= value == SpotContingencyType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot list status type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotListStatusType(ref Utf8JsonReader reader, out SpotListStatusType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotListStatusType.UNKNOWN;
            return false;
        }

        value = ToSpotListStatusType(reader.ValueSpan);
        nonStandard |= value == SpotListStatusType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot list order status. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotListOrderStatus(ref Utf8JsonReader reader, out SpotListOrderStatus value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotListOrderStatus.UNKNOWN;
            return false;
        }

        value = ToSpotListOrderStatus(reader.ValueSpan);
        nonStandard |= value == SpotListOrderStatus.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot working floor. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotWorkingFloor(ref Utf8JsonReader reader, out SpotWorkingFloor value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotWorkingFloor.UNKNOWN;
            return false;
        }

        value = ToSpotWorkingFloor(reader.ValueSpan);
        nonStandard |= value == SpotWorkingFloor.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot peg price type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotPegPriceType(ref Utf8JsonReader reader, out SpotPegPriceType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotPegPriceType.UNKNOWN;
            return false;
        }

        value = ToSpotPegPriceType(reader.ValueSpan);
        nonStandard |= value == SpotPegPriceType.UNKNOWN;
        return true;
    }

    /// <summary>Reads a spot peg offset type. An unknown one reads as UNKNOWN and sets nonStandard.</summary>
    public static bool TryReadSpotPegOffsetType(ref Utf8JsonReader reader, out SpotPegOffsetType value, ref bool nonStandard)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            value = SpotPegOffsetType.UNKNOWN;
            return false;
        }

        value = ToSpotPegOffsetType(reader.ValueSpan);
        nonStandard |= value == SpotPegOffsetType.UNKNOWN;
        return true;
    }

    private static OrderSide ToOrderSide(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("BUY"u8)) return OrderSide.BUY;
        if (text.SequenceEqual("SELL"u8)) return OrderSide.SELL;

        return OrderSide.UNKNOWN;
    }

    private static OrderType ToOrderType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("LIMIT"u8)) return OrderType.LIMIT;
        if (text.SequenceEqual("MARKET"u8)) return OrderType.MARKET;
        if (text.SequenceEqual("STOP"u8)) return OrderType.STOP;
        if (text.SequenceEqual("STOP_MARKET"u8)) return OrderType.STOP_MARKET;
        if (text.SequenceEqual("TAKE_PROFIT"u8)) return OrderType.TAKE_PROFIT;
        if (text.SequenceEqual("TAKE_PROFIT_MARKET"u8)) return OrderType.TAKE_PROFIT_MARKET;
        if (text.SequenceEqual("TRAILING_STOP_MARKET"u8)) return OrderType.TRAILING_STOP_MARKET;
        if (text.SequenceEqual("LIQUIDATION"u8)) return OrderType.LIQUIDATION;

        return OrderType.UNKNOWN;
    }

    private static ExecutionType ToExecutionType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NEW"u8)) return ExecutionType.NEW;
        if (text.SequenceEqual("TRADE"u8)) return ExecutionType.TRADE;
        if (text.SequenceEqual("CANCELED"u8)) return ExecutionType.CANCELED;
        if (text.SequenceEqual("EXPIRED"u8)) return ExecutionType.EXPIRED;
        if (text.SequenceEqual("CALCULATED"u8)) return ExecutionType.CALCULATED;
        if (text.SequenceEqual("AMENDMENT"u8)) return ExecutionType.AMENDMENT;

        return ExecutionType.UNKNOWN;
    }

    private static OrderStatus ToOrderStatus(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NEW"u8)) return OrderStatus.NEW;
        if (text.SequenceEqual("PARTIALLY_FILLED"u8)) return OrderStatus.PARTIALLY_FILLED;
        if (text.SequenceEqual("FILLED"u8)) return OrderStatus.FILLED;
        if (text.SequenceEqual("CANCELED"u8)) return OrderStatus.CANCELED;
        if (text.SequenceEqual("EXPIRED"u8)) return OrderStatus.EXPIRED;
        if (text.SequenceEqual("EXPIRED_IN_MATCH"u8)) return OrderStatus.EXPIRED_IN_MATCH;
        if (text.SequenceEqual("REJECTED"u8)) return OrderStatus.REJECTED;

        return OrderStatus.UNKNOWN;
    }

    private static TimeInForce ToTimeInForce(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("GTC"u8)) return TimeInForce.GTC;
        if (text.SequenceEqual("IOC"u8)) return TimeInForce.IOC;
        if (text.SequenceEqual("FOK"u8)) return TimeInForce.FOK;
        if (text.SequenceEqual("GTX"u8)) return TimeInForce.GTX;
        if (text.SequenceEqual("GTD"u8)) return TimeInForce.GTD;
        if (text.SequenceEqual("RPI"u8)) return TimeInForce.RPI;

        return TimeInForce.UNKNOWN;
    }

    private static WorkingType ToWorkingType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("CONTRACT_PRICE"u8)) return WorkingType.CONTRACT_PRICE;
        if (text.SequenceEqual("MARK_PRICE"u8)) return WorkingType.MARK_PRICE;

        return WorkingType.UNKNOWN;
    }

    // ACCOUNT_UPDATE writes cross and isolated, MARGIN_CALL writes CROSSED and ISOLATED.
    private static MarginType ToMarginType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("cross"u8) || text.SequenceEqual("CROSSED"u8)) return MarginType.CROSS;
        if (text.SequenceEqual("isolated"u8) || text.SequenceEqual("ISOLATED"u8)) return MarginType.ISOLATED;

        return MarginType.UNKNOWN;
    }

    private static AccountUpdateReason ToAccountUpdateReason(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("ORDER"u8)) return AccountUpdateReason.ORDER;
        if (text.SequenceEqual("FUNDING_FEE"u8)) return AccountUpdateReason.FUNDING_FEE;
        if (text.SequenceEqual("DEPOSIT"u8)) return AccountUpdateReason.DEPOSIT;
        if (text.SequenceEqual("WITHDRAW"u8)) return AccountUpdateReason.WITHDRAW;
        if (text.SequenceEqual("WITHDRAW_REJECT"u8)) return AccountUpdateReason.WITHDRAW_REJECT;
        if (text.SequenceEqual("ADJUSTMENT"u8)) return AccountUpdateReason.ADJUSTMENT;
        if (text.SequenceEqual("INSURANCE_CLEAR"u8)) return AccountUpdateReason.INSURANCE_CLEAR;
        if (text.SequenceEqual("ADMIN_DEPOSIT"u8)) return AccountUpdateReason.ADMIN_DEPOSIT;
        if (text.SequenceEqual("ADMIN_WITHDRAW"u8)) return AccountUpdateReason.ADMIN_WITHDRAW;
        if (text.SequenceEqual("MARGIN_TRANSFER"u8)) return AccountUpdateReason.MARGIN_TRANSFER;
        if (text.SequenceEqual("MARGIN_TYPE_CHANGE"u8)) return AccountUpdateReason.MARGIN_TYPE_CHANGE;
        if (text.SequenceEqual("ASSET_TRANSFER"u8)) return AccountUpdateReason.ASSET_TRANSFER;
        if (text.SequenceEqual("OPTIONS_PREMIUM_FEE"u8)) return AccountUpdateReason.OPTIONS_PREMIUM_FEE;
        if (text.SequenceEqual("OPTIONS_SETTLE_PROFIT"u8)) return AccountUpdateReason.OPTIONS_SETTLE_PROFIT;
        if (text.SequenceEqual("AUTO_EXCHANGE"u8)) return AccountUpdateReason.AUTO_EXCHANGE;
        if (text.SequenceEqual("COIN_SWAP_DEPOSIT"u8)) return AccountUpdateReason.COIN_SWAP_DEPOSIT;
        if (text.SequenceEqual("COIN_SWAP_WITHDRAW"u8)) return AccountUpdateReason.COIN_SWAP_WITHDRAW;

        return AccountUpdateReason.UNKNOWN;
    }

    private static AlgoStatus ToAlgoStatus(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NEW"u8)) return AlgoStatus.NEW;
        if (text.SequenceEqual("CANCELED"u8)) return AlgoStatus.CANCELED;
        if (text.SequenceEqual("TRIGGERING"u8)) return AlgoStatus.TRIGGERING;
        if (text.SequenceEqual("TRIGGERED"u8)) return AlgoStatus.TRIGGERED;
        if (text.SequenceEqual("FINISHED"u8)) return AlgoStatus.FINISHED;
        if (text.SequenceEqual("REJECTED"u8)) return AlgoStatus.REJECTED;
        if (text.SequenceEqual("EXPIRED"u8)) return AlgoStatus.EXPIRED;

        return AlgoStatus.UNKNOWN;
    }

    private static AlgoType ToAlgoType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("CONDITIONAL"u8)) return AlgoType.CONDITIONAL;

        return AlgoType.UNKNOWN;
    }

    private static PriceMatch ToPriceMatch(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NONE"u8)) return PriceMatch.NONE;
        if (text.SequenceEqual("OPPONENT"u8)) return PriceMatch.OPPONENT;
        if (text.SequenceEqual("OPPONENT_5"u8)) return PriceMatch.OPPONENT_5;
        if (text.SequenceEqual("OPPONENT_10"u8)) return PriceMatch.OPPONENT_10;
        if (text.SequenceEqual("OPPONENT_20"u8)) return PriceMatch.OPPONENT_20;
        if (text.SequenceEqual("QUEUE"u8)) return PriceMatch.QUEUE;
        if (text.SequenceEqual("QUEUE_5"u8)) return PriceMatch.QUEUE_5;
        if (text.SequenceEqual("QUEUE_10"u8)) return PriceMatch.QUEUE_10;
        if (text.SequenceEqual("QUEUE_20"u8)) return PriceMatch.QUEUE_20;

        return PriceMatch.UNKNOWN;
    }

    private static SelfTradePreventionMode ToSelfTradePreventionMode(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NONE"u8)) return SelfTradePreventionMode.NONE;
        if (text.SequenceEqual("EXPIRE_TAKER"u8)) return SelfTradePreventionMode.EXPIRE_TAKER;
        if (text.SequenceEqual("EXPIRE_MAKER"u8)) return SelfTradePreventionMode.EXPIRE_MAKER;
        if (text.SequenceEqual("EXPIRE_BOTH"u8)) return SelfTradePreventionMode.EXPIRE_BOTH;

        return SelfTradePreventionMode.UNKNOWN;
    }

    private static SymbolStatus ToSymbolStatus(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("TRADING"u8)) return SymbolStatus.TRADING;
        if (text.SequenceEqual("PENDING_TRADING"u8)) return SymbolStatus.PENDING_TRADING;
        if (text.SequenceEqual("PRE_DELIVERING"u8)) return SymbolStatus.PRE_DELIVERING;
        if (text.SequenceEqual("DELIVERING"u8)) return SymbolStatus.DELIVERING;
        if (text.SequenceEqual("DELIVERED"u8)) return SymbolStatus.DELIVERED;
        if (text.SequenceEqual("PRE_SETTLE"u8)) return SymbolStatus.PRE_SETTLE;
        if (text.SequenceEqual("SETTLING"u8)) return SymbolStatus.SETTLING;
        if (text.SequenceEqual("CLOSE"u8)) return SymbolStatus.CLOSE;
        if (text.SequenceEqual("TRADING_HALT"u8)) return SymbolStatus.TRADING_HALT;
        if (text.SequenceEqual("TRADING_CANCEL_ONLY"u8)) return SymbolStatus.TRADING_CANCEL_ONLY;

        return SymbolStatus.UNKNOWN;
    }

    private static SpotOrderType ToSpotOrderType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("LIMIT"u8)) return SpotOrderType.LIMIT;
        if (text.SequenceEqual("MARKET"u8)) return SpotOrderType.MARKET;
        if (text.SequenceEqual("LIMIT_MAKER"u8)) return SpotOrderType.LIMIT_MAKER;
        if (text.SequenceEqual("STOP_LOSS"u8)) return SpotOrderType.STOP_LOSS;
        if (text.SequenceEqual("STOP_LOSS_LIMIT"u8)) return SpotOrderType.STOP_LOSS_LIMIT;
        if (text.SequenceEqual("TAKE_PROFIT"u8)) return SpotOrderType.TAKE_PROFIT;
        if (text.SequenceEqual("TAKE_PROFIT_LIMIT"u8)) return SpotOrderType.TAKE_PROFIT_LIMIT;

        return SpotOrderType.UNKNOWN;
    }

    private static SpotOrderStatus ToSpotOrderStatus(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NEW"u8)) return SpotOrderStatus.NEW;
        if (text.SequenceEqual("PARTIALLY_FILLED"u8)) return SpotOrderStatus.PARTIALLY_FILLED;
        if (text.SequenceEqual("FILLED"u8)) return SpotOrderStatus.FILLED;
        if (text.SequenceEqual("CANCELED"u8)) return SpotOrderStatus.CANCELED;
        if (text.SequenceEqual("EXPIRED"u8)) return SpotOrderStatus.EXPIRED;
        if (text.SequenceEqual("EXPIRED_IN_MATCH"u8)) return SpotOrderStatus.EXPIRED_IN_MATCH;
        if (text.SequenceEqual("REJECTED"u8)) return SpotOrderStatus.REJECTED;
        if (text.SequenceEqual("PENDING_NEW"u8)) return SpotOrderStatus.PENDING_NEW;
        if (text.SequenceEqual("PENDING_CANCEL"u8)) return SpotOrderStatus.PENDING_CANCEL;

        return SpotOrderStatus.UNKNOWN;
    }

    private static SpotExecutionType ToSpotExecutionType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NEW"u8)) return SpotExecutionType.NEW;
        if (text.SequenceEqual("TRADE"u8)) return SpotExecutionType.TRADE;
        if (text.SequenceEqual("CANCELED"u8)) return SpotExecutionType.CANCELED;
        if (text.SequenceEqual("EXPIRED"u8)) return SpotExecutionType.EXPIRED;
        if (text.SequenceEqual("REPLACED"u8)) return SpotExecutionType.REPLACED;
        if (text.SequenceEqual("REJECTED"u8)) return SpotExecutionType.REJECTED;
        if (text.SequenceEqual("TRADE_PREVENTION"u8)) return SpotExecutionType.TRADE_PREVENTION;

        return SpotExecutionType.UNKNOWN;
    }

    private static SpotSelfTradePreventionMode ToSpotSelfTradePreventionMode(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NONE"u8)) return SpotSelfTradePreventionMode.NONE;
        if (text.SequenceEqual("EXPIRE_MAKER"u8)) return SpotSelfTradePreventionMode.EXPIRE_MAKER;
        if (text.SequenceEqual("EXPIRE_TAKER"u8)) return SpotSelfTradePreventionMode.EXPIRE_TAKER;
        if (text.SequenceEqual("EXPIRE_BOTH"u8)) return SpotSelfTradePreventionMode.EXPIRE_BOTH;
        if (text.SequenceEqual("DECREMENT"u8)) return SpotSelfTradePreventionMode.DECREMENT;
        if (text.SequenceEqual("TRANSFER"u8)) return SpotSelfTradePreventionMode.TRANSFER;

        return SpotSelfTradePreventionMode.UNKNOWN;
    }

    private static SpotExpiryReason ToSpotExpiryReason(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("NONE"u8)) return SpotExpiryReason.NONE;
        if (text.SequenceEqual("UNFILLED_IOC_QUANTITY_EXPIRED"u8)) return SpotExpiryReason.UNFILLED_IOC_QUANTITY_EXPIRED;
        if (text.SequenceEqual("UNFILLED_FOK_ORDER_EXPIRED"u8)) return SpotExpiryReason.UNFILLED_FOK_ORDER_EXPIRED;
        if (text.SequenceEqual("INSUFFICIENT_LIQUIDITY"u8)) return SpotExpiryReason.INSUFFICIENT_LIQUIDITY;
        if (text.SequenceEqual("EXCHANGE_CANCELED"u8)) return SpotExpiryReason.EXCHANGE_CANCELED;
        if (text.SequenceEqual("OCO_TRIGGER"u8)) return SpotExpiryReason.OCO_TRIGGER;
        if (text.SequenceEqual("OTO_PHASE_ONE_EXPIRED"u8)) return SpotExpiryReason.OTO_PHASE_ONE_EXPIRED;
        if (text.SequenceEqual("REJECTED"u8)) return SpotExpiryReason.REJECTED;
        if (text.SequenceEqual("EXECUTION_RULE_PRICE_RANGE_EXCEEDED"u8)) return SpotExpiryReason.EXECUTION_RULE_PRICE_RANGE_EXCEEDED;

        return SpotExpiryReason.UNKNOWN;
    }

    private static SpotContingencyType ToSpotContingencyType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("OCO"u8)) return SpotContingencyType.OCO;
        if (text.SequenceEqual("OTO"u8)) return SpotContingencyType.OTO;

        return SpotContingencyType.UNKNOWN;
    }

    private static SpotListStatusType ToSpotListStatusType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("EXEC_STARTED"u8)) return SpotListStatusType.EXEC_STARTED;
        if (text.SequenceEqual("ALL_DONE"u8)) return SpotListStatusType.ALL_DONE;
        if (text.SequenceEqual("UPDATED"u8)) return SpotListStatusType.UPDATED;
        if (text.SequenceEqual("RESPONSE"u8)) return SpotListStatusType.RESPONSE;

        return SpotListStatusType.UNKNOWN;
    }

    private static SpotListOrderStatus ToSpotListOrderStatus(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("EXECUTING"u8)) return SpotListOrderStatus.EXECUTING;
        if (text.SequenceEqual("ALL_DONE"u8)) return SpotListOrderStatus.ALL_DONE;
        if (text.SequenceEqual("REJECT"u8)) return SpotListOrderStatus.REJECT;

        return SpotListOrderStatus.UNKNOWN;
    }

    private static SpotWorkingFloor ToSpotWorkingFloor(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("EXCHANGE"u8)) return SpotWorkingFloor.EXCHANGE;
        if (text.SequenceEqual("SOR"u8)) return SpotWorkingFloor.SOR;

        return SpotWorkingFloor.UNKNOWN;
    }

    private static SpotPegPriceType ToSpotPegPriceType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("PRIMARY_PEG"u8)) return SpotPegPriceType.PRIMARY_PEG;
        if (text.SequenceEqual("MARKET_PEG"u8)) return SpotPegPriceType.MARKET_PEG;

        return SpotPegPriceType.UNKNOWN;
    }

    private static SpotPegOffsetType ToSpotPegOffsetType(ReadOnlySpan<byte> text)
    {
        if (text.SequenceEqual("PRICE_LEVEL"u8)) return SpotPegOffsetType.PRICE_LEVEL;

        return SpotPegOffsetType.UNKNOWN;
    }
}
