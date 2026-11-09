using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Samples.Handlers;

namespace Axitor.Trading.Gateway.Samples.Setup;

/// <summary>
/// How to create the client. Create one client per app, keep it as a singleton and dispose it when the app stops.
/// Pass your app shutdown token: it stops every connection of the client.
/// </summary>
internal static class ClientSetupSamples
{
    /// <summary>The short way: API key, secret and default options.</summary>
    public static BinanceClient<NoOpTelemetryHandler> CreateWithKeys(CancellationToken appToken)
    {
        // Never put keys in code. Read them from the environment or a secret store.
        string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
        string? apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");

        // NoOpTelemetryHandler ignores all reports. See CreateWithTelemetry to get them.
        return new BinanceClient<NoOpTelemetryHandler>(apiKey, apiSecret, appCancellationToken: appToken);
    }

    /// <summary>No keys: only public endpoints work, e.g. klines, order book and market streams.</summary>
    public static BinanceClient<NoOpTelemetryHandler> CreatePublicOnly(CancellationToken appToken) =>
        new(key: null, secret: null, appCancellationToken: appToken);

    /// <summary>Full control over the options.</summary>
    public static BinanceClient<NoOpTelemetryHandler> CreateWithOptions(CancellationToken appToken)
    {
        BinanceOptions options = new()
        {
            Secret = new BinanceSecret(
                Environment.GetEnvironmentVariable("BINANCE_API_KEY"),
                Environment.GetEnvironmentVariable("BINANCE_API_SECRET"))
            {
                // Needed only for spot SBE streams.
                Ed25519ApiKey = Environment.GetEnvironmentVariable("BINANCE_ED25519_API_KEY"),
            },

            // WebSocket API connections of these markets open when the client is created.
            WsApiMarkets = BinanceMarkets.FuturesUsdM | BinanceMarkets.Spot,

            // Streams of these markets can be used. Their connections open on the first subscription.
            StreamMarkets = BinanceMarkets.FuturesUsdM | BinanceMarkets.FuturesCoinM | BinanceMarkets.Spot,

            // Rate limits of your account tier. The library does not fetch them, so set them right.
            Limits = new BinanceLimits()
            {
                FuturesRestApiLimitRequestWeightPerMinute = 2400,
                FuturesRestApiLimitOrderWeightPerMinute = 1200,
                FuturesRestApiLimitOrderWeightPer10s = 300,
                RecvWindowMilliseconds = 5000,
            },
        };

        // Timeouts, reconnects and connection counts. The defaults fit most apps.
        EngineOptions defaults = new();

        // Example: more stream connections hold more streams without adding latency.
        EngineOptions engineOptions = defaults with
        {
            WsStream = defaults.WsStream with
            {
                Network = defaults.WsStream.Network with { MaxConnections = 10 },
            },
        };

        return new BinanceClient<NoOpTelemetryHandler>(options, engineOptions, appCancellationToken: appToken);
    }

    /// <summary>Your own telemetry handler gets failures, retries and reconnects.</summary>
    public static BinanceClient<SampleTelemetryHandler> CreateWithTelemetry(CancellationToken appToken)
    {
        string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
        string? apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");

        return new BinanceClient<SampleTelemetryHandler>(apiKey, apiSecret, new SampleTelemetryHandler(), appToken);
    }

    /// <summary>Call once when the app stops. It closes every connection of the client.</summary>
    public static void Shutdown(BinanceClient<NoOpTelemetryHandler> client) => client.Dispose();
}
