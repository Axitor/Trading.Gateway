using System.Runtime.InteropServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Models.Common.Extensions;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.MarketData.KlinesData;

/// <summary>Klines/Candlestick Data endpoint payload writer.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct KlinesDataPayload : IPayloadWriter
{
    // DESIGN NOTE: 64 bytes. Pack = 8 places the 16-byte aligned Symbol at offset 8,
    // so it adds no 8 bytes of padding after the reference.

    public byte[] Host { get; init; }              // 8 bytes (Reference)

    public Symbol Symbol { get; init; }            // 16 bytes (UInt128)
    public long? StartTimeMs { get; init; }        // 16 bytes (Nullable<long>)
    public long? EndTimeMs { get; init; }          // 16 bytes (Nullable<long>)

    public int Limit { get; init; }                // 4 bytes (int32)
    public BinanceInterval Interval { get; init; } // 1 byte  (enum : byte)

    // + 3 bytes tail padding to 64.

    /// <inheritdoc />
    public void Write(ref PayloadBuilder builder)
    {
        builder.Append(Host);
        builder.Append("/fapi/v1/klines"u8);

        builder.Append("symbol="u8, Symbol);
        builder.Append("interval="u8, Interval.ToUtf8Bytes());
        builder.Append("limit="u8, Limit);

        builder.AppendOptional("startTime="u8, StartTimeMs);
        builder.AppendOptional("endTime="u8, EndTimeMs);
    }
}
