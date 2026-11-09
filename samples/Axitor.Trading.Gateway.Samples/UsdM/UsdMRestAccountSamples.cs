using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.LeverageBracket;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.RestApi.Account.SymbolConfig;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>USD-M futures REST account. Signed endpoints: the client needs an API key and secret.</summary>
internal static class UsdMRestAccountSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Balances and open positions. Lists only symbols with a position or an open order.</summary>
    public static async Task AccountInfoAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<AccountInfoModel> result = await client.Futures.UsdM.RestApi.Account.AccountInfo.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Account info failed: {result.Errors.Reason}");
            return;
        }

        AccountInfoModel account = result.Value;
        Console.WriteLine($"Wallet {account.TotalWalletBalance}, available {account.AvailableBalance}");

        foreach (AccountPosition position in account.Positions)
        {
            // Positive quantity is long, negative is short.
            Console.WriteLine($"{position.Symbol} {position.Side}: {position.Quantity}, profit {position.UnrealizedProfit}");
        }
    }

    /// <summary>Balances and every symbol of the market with its leverage and margin mode. Large answer: call it rarely.</summary>
    public static async Task AccountInfoV2Async(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<AccountInfoV2Model> result = await client.Futures.UsdM.RestApi.Account.AccountInfoV2.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Account info V2 failed: {result.Errors.Reason}");
            return;
        }

        AccountInfoV2Model account = result.Value;
        Console.WriteLine($"Fee tier {account.FeeTier}, can trade {account.CanTrade}, wallet {account.TotalWalletBalance}");

        foreach (AccountInfoV2Position position in account.Positions)
        {
            // Every symbol is listed. Skip the ones with no position.
            if (position.Quantity == 0)
            {
                continue;
            }

            Console.WriteLine($"{position.Symbol}: {position.Quantity} at {position.EntryPrice}, leverage {position.Leverage}, isolated {position.IsIsolated}");
        }
    }

    /// <summary>Margin type, leverage and largest notional allowed of one symbol.</summary>
    public static async Task SymbolConfigAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<SymbolConfigModel> result = await client.Futures.UsdM.RestApi.Account.SymbolConfig.GetAsync(_symbol).ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Symbol config failed: {result.Errors.Reason}");
            return;
        }

        foreach (SymbolConfigEntry entry in result.Value.Symbols)
        {
            Console.WriteLine($"{entry.Symbol}: {entry.MarginType}, leverage {entry.Leverage}, max notional {entry.MaxNotionalValue}");
        }
    }

    /// <summary>Margin type, leverage and largest notional allowed of every symbol.</summary>
    public static async Task SymbolConfigAllAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<SymbolConfigModel> result = await client.Futures.UsdM.RestApi.Account.SymbolConfig.GetAllAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Symbol config failed: {result.Errors.Reason}");
            return;
        }

        Console.WriteLine($"{result.Value.Symbols.Length} symbols");
    }

    /// <summary>Leverage brackets of one symbol: the highest leverage for each notional range.</summary>
    public static async Task LeverageBracketAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<LeverageBracketModel> result = await client.Futures.UsdM.RestApi.Account.LeverageBracket.GetAsync(_symbol).ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Leverage brackets failed: {result.Errors.Reason}");
            return;
        }

        foreach (SymbolBrackets symbol in result.Value.Symbols)
        {
            foreach (LeverageBracketLevel level in symbol.Brackets)
            {
                Console.WriteLine($"{symbol.Symbol} bracket {level.Bracket}: up to {level.NotionalCap} at leverage {level.InitialLeverage}, maint ratio {level.MaintMarginRatio}");
            }
        }
    }

    /// <summary>Leverage brackets of every symbol. Large answer: call it rarely.</summary>
    public static async Task LeverageBracketAllAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        RestApiRequestResult<LeverageBracketModel> result = await client.Futures.UsdM.RestApi.Account.LeverageBracket.GetAllAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Leverage brackets failed: {result.Errors.Reason}");
            return;
        }

        Console.WriteLine($"{result.Value.Symbols.Length} symbols");
    }
}
