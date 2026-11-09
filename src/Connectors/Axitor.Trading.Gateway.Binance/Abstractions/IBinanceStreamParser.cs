namespace Axitor.Trading.Gateway.Binance.Abstractions;

/// <summary>Parses the data of one stream type. Implement as a readonly struct.</summary>
internal interface IBinanceStreamParser<TModel> where TModel : struct
{
    /// <summary>Reads the data into the model. The update id grows with every message and drops duplicates. False when the data has an unexpected shape.</summary>
    static abstract bool TryParse(ReadOnlySpan<byte> data, out TModel model, out long updateId);
}
