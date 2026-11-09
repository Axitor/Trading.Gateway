namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>How often a spot depth stream pushes.</summary>
public enum SpotDepthUpdateSpeed : byte
{
    /// <summary>Every 1000 milliseconds, the exchange default.</summary>
    Ms1000,

    /// <summary>Every 100 milliseconds.</summary>
    Ms100,
}
