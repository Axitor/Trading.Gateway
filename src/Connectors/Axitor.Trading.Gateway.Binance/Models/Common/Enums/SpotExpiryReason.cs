using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Why a spot order expired.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SpotExpiryReason : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Did not expire.</summary>
    NONE,

    /// <summary>A contingent order or an order of an OTO was rejected when placed on the book, e.g. lack of funds.</summary>
    REJECTED,

    /// <summary>Canceled by the exchange.</summary>
    EXCHANGE_CANCELED,

    /// <summary>The other order of its OCO pair started working, or the whole OCO expired.</summary>
    OCO_TRIGGER,

    /// <summary>The working order of its order list expired, so the whole list expired.</summary>
    OTO_PHASE_ONE_EXPIRED,

    /// <summary>IOC order not filled in full; the rest expired.</summary>
    UNFILLED_IOC_QUANTITY_EXPIRED,

    /// <summary>FOK order could not be filled in full.</summary>
    UNFILLED_FOK_ORDER_EXPIRED,

    /// <summary>Not enough orders in the book to match.</summary>
    INSUFFICIENT_LIQUIDITY,

    /// <summary>The trade price would break the price range execution rule.</summary>
    EXECUTION_RULE_PRICE_RANGE_EXCEEDED,
}
