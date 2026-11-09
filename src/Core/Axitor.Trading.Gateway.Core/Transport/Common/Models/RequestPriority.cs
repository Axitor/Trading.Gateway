namespace Axitor.Trading.Gateway.Core.Transport.Common.Models;

/// <summary>Defines the urgency of a request, determining its access to connection pools.</summary>
public enum RequestPriority : byte
{
    /// <summary>
    /// Standard traffic (e.g., market data, account queries). 
    /// Draws exclusively from the normal connection pool and waits in the standard queue if it is exhausted.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Time-sensitive operations (e.g., placing or canceling orders). 
    /// Attempts to use a normal slot first, but falls back to a dedicated critical reserve to bypass the standard queue.
    /// </summary>
    Critical = 1,
}
