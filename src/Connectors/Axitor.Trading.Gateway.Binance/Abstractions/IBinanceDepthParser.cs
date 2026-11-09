using Axitor.Trading.Gateway.Binance.Models.Depth;

namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Parses diff depth updates of one market. Implement as a readonly struct.</summary>
internal interface IBinanceDepthParser
{
    /// <summary>Reads an update into the header and the level buffers; a full buffer is replaced by one twice as large. False when the data has an unexpected shape.</summary>
    static abstract bool TryParse(
        ReadOnlySpan<byte> data,
        out DepthUpdateHeader header,
        ref DepthLevel[] bids,
        out int bidCount,
        ref DepthLevel[] asks,
        out int askCount);
}
