using System.Text.Json.Serialization;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Binance.Models.MarketData;

namespace Axitor.Trading.Gateway.Binance.Serialization;

/// <summary>
/// Source-generated serializer metadata for response shapes this connector binds by name.
/// Compile-time generated: no reflection, no runtime code generation, Native AOT safe.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    IncludeFields = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(ServerTimeModel))]
[JsonSerializable(typeof(TickerPriceModel))]
[JsonSerializable(typeof(Axitor.Trading.Gateway.Binance.EndPoints.Spot.WebSockets.Api.MarketData.TickerPrice.TickerPriceModel), TypeInfoPropertyName = "SpotTickerPriceModel")]
[JsonSerializable(typeof(AccountBalanceAsset[]))]
internal sealed partial class BinanceJsonContext : JsonSerializerContext;
