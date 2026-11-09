namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Levels per side a partial depth stream sends.</summary>
public enum PartialDepthLevels : byte
{
    /// <summary>Top 5 levels.</summary>
    Five = 5,

    /// <summary>Top 10 levels.</summary>
    Ten = 10,

    /// <summary>Top 20 levels.</summary>
    Twenty = 20,
}
