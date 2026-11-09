using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Extensions;

/// <summary>Wire representation of BinanceInterval.</summary>
internal static class BinanceIntervalExtensions
{
    /// <summary>Returns the exchange's own spelling as UTF-8 bytes.</summary>
    public static ReadOnlySpan<byte> ToUtf8Bytes(this BinanceInterval interval) =>
        interval switch
        {
            BinanceInterval.OneSecond => "1s"u8,
            BinanceInterval.OneMinute => "1m"u8,
            BinanceInterval.ThreeMinutes => "3m"u8,
            BinanceInterval.FiveMinutes => "5m"u8,
            BinanceInterval.FifteenMinutes => "15m"u8,
            BinanceInterval.ThirtyMinutes => "30m"u8,
            BinanceInterval.OneHour => "1h"u8,
            BinanceInterval.TwoHours => "2h"u8,
            BinanceInterval.FourHours => "4h"u8,
            BinanceInterval.SixHours => "6h"u8,
            BinanceInterval.EightHours => "8h"u8,
            BinanceInterval.TwelveHours => "12h"u8,
            BinanceInterval.OneDay => "1d"u8,
            BinanceInterval.ThreeDays => "3d"u8,
            BinanceInterval.OneWeek => "1w"u8,
            BinanceInterval.OneMonth => "1M"u8,
            _ => ""u8,
        };
}
