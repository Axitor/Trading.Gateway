using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Orders;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.ExchangeInfo;

/// <summary>Exchange Information Endpoint Parser. Reads only the symbols, their status and the filters orders are checked by.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: A filter value that cannot be read throws: a rule silently read as zero would turn it off for every order.
/// An unknown status reads as UNKNOWN, and a symbol whose name does not fit Symbol is left out.
/// </para>
/// <para>
/// DESIGN NOTE: Symbols are collected in a pooled buffer and copied out once at their exact size.
/// A growing list would allocate twice the data, and its largest buffer would land on the large object heap and bring gen 2 collections.
/// </para>
/// </remarks>
internal readonly struct ExchangeInfoParser : IResponseParser<ExchangeInfoModel>
{
    // Above the number of listed symbols, so the buffer rarely grows.
    private const int InitialSymbols = 1024;

    // The whole answer is over a megabyte: the body buffer starts at its size instead of doubling up to it.
    private const int ExpectedBodyBytes = 2 * 1024 * 1024;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ExchangeInfoModel> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken, ExpectedBodyBytes).ConfigureAwait(false);

        try
        {
            return Parse(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    private static ExchangeInfoModel Parse(ReadOnlySpan<byte> body)
    {
        Utf8JsonReader reader = new(body);
        ExchangeSymbol[]? symbols = null;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("symbols"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    ThrowMalformed();
                }

                symbols = ReadSymbols(ref reader);
            }
            else
            {
                reader.Read();
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        if (symbols is null)
        {
            ThrowMalformed();
        }

        return new ExchangeInfoModel() { Symbols = symbols };
    }

    /// <summary>Reads the symbol array the reader is on, up to its closing bracket.</summary>
    private static ExchangeSymbol[] ReadSymbols(ref Utf8JsonReader reader)
    {
        ExchangeSymbol[] scratch = ArrayPool<ExchangeSymbol>.Shared.Rent(InitialSymbols);
        int count = 0;

        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                if (count == scratch.Length)
                {
                    scratch = Grow(scratch);
                }

                if (TryReadSymbol(ref reader, out scratch[count]))
                {
                    count++;
                }
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                ThrowMalformed();
            }

            return scratch.AsSpan(0, count).ToArray();
        }
        finally
        {
            ArrayPool<ExchangeSymbol>.Shared.Return(scratch);
        }
    }

    /// <summary>Reads the symbol object the reader is on, up to its closing brace. False when its name does not fit Symbol.</summary>
    private static bool TryReadSymbol(ref Utf8JsonReader reader, out ExchangeSymbol symbol)
    {
        symbol = default;
        bool fits = false;

        // A response has no telemetry path: an unknown status just reads as UNKNOWN.
        bool nonStandard = false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("symbol"u8))
            {
                reader.Read();
                fits = BinanceJsonFieldReader.TryReadSymbol(ref reader, out symbol.Symbol);
            }
            else if (reader.ValueTextEquals("status"u8))
            {
                reader.Read();
                _ = BinanceJsonFieldReader.TryReadSymbolStatus(ref reader, out symbol.Status, ref nonStandard);
            }
            else if (reader.ValueTextEquals("filters"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    ThrowMalformed();
                }

                ReadFilters(ref reader, ref symbol.Filters);
            }
            else
            {
                reader.Read();
            }

            reader.Skip();
        }

        return fits;
    }

    /// <summary>Reads the filter array the reader is on, up to its closing bracket.</summary>
    private static void ReadFilters(ref Utf8JsonReader reader, ref SymbolFilters filters)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            ReadFilter(ref reader, ref filters);
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            ThrowMalformed();
        }
    }

    /// <summary>Reads one filter object up to its closing brace. Its type may come after its values, so they are applied at the end.</summary>
    private static void ReadFilter(ref Utf8JsonReader reader, ref SymbolFilters filters)
    {
        FilterKind kind = FilterKind.Other;
        double minPrice = 0;
        double maxPrice = 0;
        double tickSize = 0;
        double minQuantity = 0;
        double maxQuantity = 0;
        double stepSize = 0;
        double notional = 0;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("filterType"u8))
            {
                reader.Read();
                kind = ReadFilterKind(ref reader);
            }
            else if (reader.ValueTextEquals("minPrice"u8))
            {
                reader.Read();
                minPrice = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("maxPrice"u8))
            {
                reader.Read();
                maxPrice = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("tickSize"u8))
            {
                reader.Read();
                tickSize = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("minQty"u8))
            {
                reader.Read();
                minQuantity = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("maxQty"u8))
            {
                reader.Read();
                maxQuantity = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("stepSize"u8))
            {
                reader.Read();
                stepSize = ReadNumber(ref reader);
            }
            else if (reader.ValueTextEquals("notional"u8))
            {
                reader.Read();
                notional = ReadNumber(ref reader);
            }
            else
            {
                reader.Read();
            }

            reader.Skip();
        }

        switch (kind)
        {
            case FilterKind.Price:
                filters.MinPrice = minPrice;
                filters.MaxPrice = maxPrice;
                filters.TickSize = tickSize;
                return;

            case FilterKind.Lot:
                filters.MinQuantity = minQuantity;
                filters.MaxQuantity = maxQuantity;
                filters.StepSize = stepSize;
                return;

            case FilterKind.MarketLot:
                filters.MarketMinQuantity = minQuantity;
                filters.MarketMaxQuantity = maxQuantity;
                filters.MarketStepSize = stepSize;
                return;

            case FilterKind.Notional:
                filters.MinNotional = notional;
                return;
        }
    }

    private static FilterKind ReadFilterKind(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return FilterKind.Other;
        }

        if (reader.ValueTextEquals("PRICE_FILTER"u8)) return FilterKind.Price;
        if (reader.ValueTextEquals("LOT_SIZE"u8)) return FilterKind.Lot;
        if (reader.ValueTextEquals("MARKET_LOT_SIZE"u8)) return FilterKind.MarketLot;
        if (reader.ValueTextEquals("MIN_NOTIONAL"u8)) return FilterKind.Notional;

        return FilterKind.Other;
    }

    private static double ReadNumber(ref Utf8JsonReader reader)
    {
        if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out double value))
        {
            ThrowMalformed();
        }

        return value;
    }

    /// <summary>Cold path. A larger pooled buffer for more symbols than any answer before.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ExchangeSymbol[] Grow(ExchangeSymbol[] scratch)
    {
        ExchangeSymbol[] larger = ArrayPool<ExchangeSymbol>.Shared.Rent(scratch.Length * 2);
        scratch.CopyTo(larger, 0);
        ArrayPool<ExchangeSymbol>.Shared.Return(scratch);
        return larger;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Exchange info response did not have the expected shape.");

    /// <summary>Filters the parser applies; the rest are skipped.</summary>
    private enum FilterKind : byte
    {
        Other,
        Price,
        Lot,
        MarketLot,
        Notional,
    }
}
