using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>
/// Side of a position, as the exchange reports it.
/// One-way mode reports every position as Both.
/// Hedge mode splits the same symbol into Long and Short.
/// </summary>
[SuppressMessage("Naming", "CA1720", Justification = "Binance Identifiers MUST contain type names.")]
public enum PositionSide : byte
{
    /// <summary>One-way mode: a single position per symbol, direction given by its quantity.</summary>
    BOTH,

    /// <summary>Hedge mode, long leg.</summary>
    LONG,

    /// <summary>Hedge mode, short leg.</summary>
    SHORT,
}
