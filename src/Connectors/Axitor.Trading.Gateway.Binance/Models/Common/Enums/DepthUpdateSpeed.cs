namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>How often a USD-M or COIN-M depth stream pushes.</summary>
public enum DepthUpdateSpeed : byte
{
    /// <summary>Every 250 milliseconds, the exchange default.</summary>
    Ms250,

    /// <summary>Every 500 milliseconds.</summary>
    Ms500,

    /// <summary>Every 100 milliseconds.</summary>
    Ms100,
}
