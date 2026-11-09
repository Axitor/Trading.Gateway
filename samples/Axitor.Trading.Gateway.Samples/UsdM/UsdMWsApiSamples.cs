using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.Account.AccountBalanceV2;
using Axitor.Trading.Gateway.Binance.EndPoints.Futures.UsdM.WebSockets.Api.MarketData.TickerPrice;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Binance.Session;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.NoOp;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.WebSockets.Api.Models;

namespace Axitor.Trading.Gateway.Samples.UsdM;

/// <summary>
/// USD-M futures WebSocket API: market data and account. Faster than REST: one open connection, no HTTP per call.
/// Needs FuturesUsdM in BinanceOptions.WsApiMarkets, which is on by default. The first call waits until the connection is open.
/// </summary>
internal static class UsdMWsApiSamples
{
    private static readonly Symbol _symbol = Symbol.Parse("BTCUSDT");

    /// <summary>Latest price of one symbol. Public: no keys needed.</summary>
    public static async Task TickerPriceAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        WsApiRequestResult<TickerPriceModel> result = await client.Futures.UsdM.WsApi.MarketData.TickerPrice.GetAsync(_symbol).ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Ticker price failed: {result.Errors.Reason}");
            return;
        }

        Console.WriteLine($"{result.Value.Symbol}: {result.Value.Price} at {result.Value.Time}");
    }

    /// <summary>Balance of each asset. Signed: the client needs an API key and secret.</summary>
    public static async Task AccountBalanceAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        WsApiRequestResult<AccountBalanceModel> result = await client.Futures.UsdM.WsApi.Account.AccountBalance.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Account balance failed: {result.Errors.Reason}");
            return;
        }

        foreach (AccountBalanceAsset asset in result.Value.Assets)
        {
            Console.WriteLine($"{asset.Asset}: wallet {asset.WalletBalance}, available {asset.AvailableBalance}");
        }
    }

    /// <summary>Balances and every symbol of the market with its leverage and margin mode. Signed. Large answer: call it rarely.</summary>
    public static async Task AccountInfoAsync(BinanceClient<NoOpTelemetryHandler> client)
    {
        WsApiRequestResult<AccountInfoV2Model> result = await client.Futures.UsdM.WsApi.Account.AccountInfo.GetAsync().ConfigureAwait(false);

        if (result.Outcome != RequestOutcome.Success)
        {
            Console.WriteLine($"Account info failed: {result.Errors.Reason}");
            return;
        }

        AccountInfoV2Model account = result.Value;
        Console.WriteLine($"Wallet {account.TotalWalletBalance}, available {account.AvailableBalance}");

        foreach (AccountInfoV2Position position in account.Positions)
        {
            // Every symbol is listed. Skip the ones with no position.
            if (position.Quantity == 0)
            {
                continue;
            }

            Console.WriteLine($"{position.Symbol}: {position.Quantity} at {position.EntryPrice}, break even {position.BreakEvenPrice}");
        }
    }
}
