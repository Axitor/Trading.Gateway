using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Trading status of a symbol.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum SymbolStatus : byte
{
    /// <summary>A value this version does not know.</summary>
    UNKNOWN,

    /// <summary>Listed, trading not started yet.</summary>
    PENDING_TRADING,

    /// <summary>Trading.</summary>
    TRADING,

    /// <summary>About to be delivered.</summary>
    PRE_DELIVERING,

    /// <summary>Being delivered.</summary>
    DELIVERING,

    /// <summary>Delivered.</summary>
    DELIVERED,

    /// <summary>About to be settled.</summary>
    PRE_SETTLE,

    /// <summary>Being settled.</summary>
    SETTLING,

    /// <summary>Closed.</summary>
    CLOSE,

    /// <summary>Trading halted.</summary>
    TRADING_HALT,

    /// <summary>Only cancels are accepted.</summary>
    TRADING_CANCEL_ONLY,
}
