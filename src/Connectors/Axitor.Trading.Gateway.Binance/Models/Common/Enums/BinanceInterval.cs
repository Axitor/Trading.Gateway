namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Chart interval, as the exchange names it.</summary>
public enum BinanceInterval : byte
{
    /// <summary>One second. REST only; not carried by the kline streams.</summary>
    OneSecond,

    /// <summary>One minute.</summary>
    OneMinute,

    /// <summary>Three minutes.</summary>
    ThreeMinutes,

    /// <summary>Five minutes.</summary>
    FiveMinutes,

    /// <summary>Fifteen minutes.</summary>
    FifteenMinutes,

    /// <summary>Thirty minutes.</summary>
    ThirtyMinutes,

    /// <summary>One hour.</summary>
    OneHour,

    /// <summary>Two hours.</summary>
    TwoHours,

    /// <summary>Four hours.</summary>
    FourHours,

    /// <summary>Six hours.</summary>
    SixHours,

    /// <summary>Eight hours.</summary>
    EightHours,

    /// <summary>Twelve hours.</summary>
    TwelveHours,

    /// <summary>One day.</summary>
    OneDay,

    /// <summary>Three days.</summary>
    ThreeDays,

    /// <summary>One week.</summary>
    OneWeek,

    /// <summary>One month.</summary>
    OneMonth,
}
