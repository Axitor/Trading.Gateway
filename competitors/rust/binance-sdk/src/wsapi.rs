//! binance-sdk WebSocket API calls, the same requests as the .NET WebSocket API benchmarks. Each call includes reading the typed answer.

use std::hint::black_box;

use axitor_bench_common::harness::{self, BoxError};
use axitor_bench_common::{bench, report};
use binance_sdk::config::ConfigurationWebsocketApi;
use binance_sdk::derivatives_trading_usds_futures::DerivativesTradingUsdsFuturesWsApi;
use binance_sdk::derivatives_trading_usds_futures::websocket_api::{
    AccountInformationParams, CancelOrderParams, FuturesAccountBalanceV2Params, NewOrderParams, NewOrderSideEnum,
    NewOrderTimeInForceEnum, NewOrderTypeEnum, SymbolPriceTickerParams,
};
use futures_util::future::join_all;
use rust_decimal::Decimal;

use crate::{API_KEY, API_SECRET, CLIENT_NAME, CLIENT_ORDER_ID, SYMBOL};

// Requests in flight at once in the burst, as in the .NET burst benchmark.
const IN_FLIGHT: u32 = 100;

// The stub answers every cancel with the recorded one and never checks the id.
const ORDER_ID: i64 = 1;

/// Arguments after "wsapi": an optional part of the benchmark names. Returns the exit code.
pub fn run(args: &[String]) -> i32 {
    let runtime = match tokio::runtime::Builder::new_multi_thread().enable_all().build() {
        Ok(runtime) => runtime,
        Err(error) => {
            eprintln!("Tokio runtime: {error}");
            return 1;
        }
    };

    match runtime.block_on(run_suite(args.first().map(String::as_str))) {
        Ok(true) => 0,
        Ok(false) => 1,
        Err(error) => {
            eprintln!("{error}");
            1
        }
    }
}

async fn run_suite(filter: Option<&str>) -> Result<bool, BoxError> {
    let configuration = ConfigurationWebsocketApi::builder()
        .api_key(API_KEY)
        .api_secret(API_SECRET)
        .ws_url(format!("ws://{}/ws-fapi/v1", harness::stub_host()))
        .build()?;

    let connection = DerivativesTradingUsdsFuturesWsApi::from_config(configuration).connect().await?;
    let client = &connection;
    let mut is_ok = true;

    println!("{}", report::REQUEST_HEADER);
    println!("{}", report::REQUEST_SEPARATOR);

    let ticker = &SymbolPriceTickerParams::builder().symbol(SYMBOL.to_string()).build()?;

    if bench::matches(filter, "TickerPrice") {
        is_ok &= bench::run(CLIENT_NAME, "TickerPrice", 1, None, move || async move {
            black_box(client.symbol_price_ticker(ticker.clone()).await?.data()?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "TickerPriceBurst") {
        is_ok &= bench::run(CLIENT_NAME, "TickerPriceBurst", IN_FLIGHT, None, move || async move {
            let calls = (0..IN_FLIGHT).map(|_| client.symbol_price_ticker(ticker.clone()));

            for answer in join_all(calls).await {
                black_box(answer?.data()?);
            }

            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "AccountBalance") {
        let params = &FuturesAccountBalanceV2Params::builder().build()?;

        is_ok &= bench::run(CLIENT_NAME, "AccountBalance", 1, None, move || async move {
            black_box(client.futures_account_balance_v2(params.clone()).await?.data()?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "AccountInfo") {
        let params = &AccountInformationParams::builder().build()?;

        is_ok &= bench::run(CLIENT_NAME, "AccountInfo", 1, None, move || async move {
            black_box(client.account_information(params.clone()).await?.data()?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "NewOrder") {
        let params = &NewOrderParams::builder(SYMBOL.to_string(), NewOrderSideEnum::Buy, NewOrderTypeEnum::Limit)
            .time_in_force(NewOrderTimeInForceEnum::Gtc)
            .quantity(Decimal::new(1, 2))
            .price(Decimal::new(60_000, 0))
            .new_client_order_id(CLIENT_ORDER_ID.to_string())
            .build()?;

        is_ok &= bench::run(CLIENT_NAME, "NewOrder", 1, None, move || async move {
            black_box(client.new_order(params.clone()).await?.data()?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "CancelOrder") {
        let params = &CancelOrderParams::builder(SYMBOL.to_string()).order_id(ORDER_ID).build()?;

        is_ok &= bench::run(CLIENT_NAME, "CancelOrder", 1, None, move || async move {
            black_box(client.cancel_order(params.clone()).await?.data()?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if let Err(error) = connection.disconnect().await {
        eprintln!("Disconnect: {error}");
    }

    Ok(is_ok)
}