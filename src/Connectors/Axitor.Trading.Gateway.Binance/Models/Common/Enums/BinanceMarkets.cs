namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Binance markets as flags. An endpoint belongs to one market; options combine several, e.g. FuturesUsdM | Spot.</summary>
[Flags]
public enum BinanceMarkets : byte
{
    /// <summary>No market. Only the rules common to every market apply.</summary>
    None = 0,

    /// <summary>USD-M futures.</summary>
    FuturesUsdM = 1 << 0,

    /// <summary>COIN-M futures.</summary>
    FuturesCoinM = 1 << 1,

    /// <summary>Spot.</summary>
    Spot = 1 << 2,
}
