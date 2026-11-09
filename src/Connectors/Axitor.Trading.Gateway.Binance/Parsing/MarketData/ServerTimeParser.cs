using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.MarketData;
using Axitor.Trading.Gateway.Binance.Serialization;
using Axitor.Trading.Gateway.Core.Abstractions;

namespace Axitor.Trading.Gateway.Binance.Parsing.MarketData;

/// <summary>Server time parser of every market.</summary>
internal readonly struct ServerTimeParser : IResponseParser<ServerTimeModel>
{
    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ServerTimeModel> ParseAsync(Stream body, CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync(body, BinanceJsonContext.Default.ServerTimeModel, cancellationToken).ConfigureAwait(false);
}
