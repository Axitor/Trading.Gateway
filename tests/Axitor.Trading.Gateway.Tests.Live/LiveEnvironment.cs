using System.Diagnostics;
using Axitor.Trading.Gateway.Binance.Configuration;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Tests.Live.Support;

namespace Axitor.Trading.Gateway.Tests.Live;

/// <summary>Keys from the environment and mainnet clients built on them.</summary>
internal static class LiveEnvironment
{
    private const string ApiKeyVariable = "BINANCE_API_KEY";
    private const string ApiSecretVariable = "BINANCE_API_SECRET";
    private const string Ed25519ApiKeyVariable = "BINANCE_ED25519_API_KEY";
    private const string TimeoutMessage = "Condition was not met within 30 s.";
    private const int StepMillis = 50;

    private static readonly string? _apiKey = Read(ApiKeyVariable);
    private static readonly string? _apiSecret = Read(ApiSecretVariable);
    private static readonly string? _ed25519ApiKey = Read(Ed25519ApiKeyVariable);

    /// <summary>Longest a live test waits for the exchange.</summary>
    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    /// <summary>Ed25519 API key from the environment. Empty when not set: call RequireEd25519Key first.</summary>
    public static string Ed25519ApiKey => _ed25519ApiKey ?? string.Empty;

    /// <summary>Skips the test when the API key or secret is not set.</summary>
    public static void RequireKeys() =>
        Assert.SkipWhen(_apiKey is null || _apiSecret is null, $"Set {ApiKeyVariable} and {ApiSecretVariable} to run signed live tests.");

    /// <summary>Skips the test when the Ed25519 API key is not set.</summary>
    public static void RequireEd25519Key() =>
        Assert.SkipWhen(_ed25519ApiKey is null, $"Set {Ed25519ApiKeyVariable} to run SBE live tests.");

    /// <summary>
    /// Mainnet client stopped by the test token. Unsigned clients get no API key and secret; markets left out open no WebSocket connection.
    /// A given apiSecret or ed25519ApiKey replaces the one from the environment.
    /// </summary>
    public static BinanceClient<LiveTelemetry> CreateClient(
        LiveTelemetryLog log,
        bool signed,
        BinanceMarkets wsApiMarkets = BinanceMarkets.None,
        BinanceMarkets streamMarkets = BinanceMarkets.None,
        string? apiSecret = null,
        string? ed25519ApiKey = null)
    {
        BinanceOptions options = new()
        {
            Secret = new BinanceSecret(signed ? _apiKey : null, signed ? apiSecret ?? _apiSecret : null) { Ed25519ApiKey = ed25519ApiKey ?? _ed25519ApiKey },
            WsApiMarkets = wsApiMarkets,
            StreamMarkets = streamMarkets,
        };

        return new BinanceClient<LiveTelemetry>(options, Engine(), new LiveTelemetry(log), TestContext.Current.CancellationToken);
    }

    /// <summary>Polls every 50 ms until the condition holds. Fails the test after the wait limit.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (!condition())
        {
            if (WaitLimit <= elapsed.Elapsed)
            {
                throw new TimeoutException(TimeoutMessage);
            }

            await Task.Delay(StepMillis, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Default engine options with exchange error texts kept, so a failed run shows what the exchange said.</summary>
    private static EngineOptions Engine()
    {
        EngineOptions defaults = new();

        return defaults with
        {
            RestApi = defaults.RestApi with
            {
                Resiliency = defaults.RestApi.Resiliency with
                {
                    CaptureErrorMessageString = true,
                    CaptureErrorDataString = true,
                    CaptureFailureRequestUri = true,
                },
            },
            WsApi = defaults.WsApi with
            {
                Resiliency = defaults.WsApi.Resiliency with
                {
                    CaptureErrorMessageString = true,
                    CaptureErrorDataString = true,
                },
            },
        };
    }

    private static string? Read(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
