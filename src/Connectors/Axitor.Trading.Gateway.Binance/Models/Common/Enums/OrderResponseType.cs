namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>What the order response carries.</summary>
public enum OrderResponseType : byte
{
    /// <summary>The order as accepted.</summary>
    ACK,

    /// <summary>The final status of a MARKET or IOC/FOK order, sent once it is known.</summary>
    RESULT,
}
