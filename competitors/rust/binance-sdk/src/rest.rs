//! binance-sdk REST calls, the same requests as the .NET REST benchmarks. Each call includes reading the typed answer.

use std::hint::black_box;

use axitor_bench_common::harness::{self, BoxError};
use axitor_bench_common::{bench, report};
use binance_sdk::config::ConfigurationRestApi;
use binance_sdk::derivatives_trading_usds_futures::DerivativesTradingUsdsFuturesRestApi;
use binance_sdk::derivatives_trading_usds_futures::rest_api::{
    AccountInformationV2Params, KlineCandlestickDataIntervalEnum, KlineCandlestickDataParams, NewOrderParams,
    NewOrderSideEnum, NewOrderTimeInForceEnum, NewOrderTypeEnum, NotionalAndLeverageBracketsParams, OrderBookParams,
};
use rust_decimal::Decimal;

use crate::{API_KEY, API_SECRET, CLIENT_NAME, CLIENT_ORDER_ID, SYMBOL};

const ORDER_BOOK_LIMIT: i64 = 1000;

/// Arguments after "rest": an optional part of the benchmark names. Returns the exit code.
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
    let configuration = ConfigurationRestApi::builder()
        .api_key(API_KEY)
        .api_secret(API_SECRET)
        .base_path(format!("http://{}", harness::stub_host()))
        .build()?;

    let client = DerivativesTradingUsdsFuturesRestApi::from_config(configuration);
    let client = &client;
    let mut is_ok = true;

    println!("{}", report::REQUEST_HEADER);
    println!("{}", report::REQUEST_SEPARATOR);

    for count in [500_i64, 1500] {
        let name = format!("Klines({count})");

        if bench::matches(filter, &name) {
            let params = &KlineCandlestickDataParams::builder(SYMBOL.to_string(), KlineCandlestickDataIntervalEnum::Interval1m)
                .limit(count)
                .build()?;

            is_ok &= bench::run(CLIENT_NAME, &name, 1, None, move || async move {
                black_box(client.kline_candlestick_data(params.clone()).await?.data().await?);
                Ok::<(), BoxError>(())
            })
            .await;
        }
    }

    if bench::matches(filter, "OrderBook") {
        let params = &OrderBookParams::builder(SYMBOL.to_string()).limit(ORDER_BOOK_LIMIT).build()?;

        is_ok &= bench::run(CLIENT_NAME, "OrderBook", 1, None, move || async move {
            black_box(client.order_book(params.clone()).await?.data().await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "ExchangeInfo") {
        is_ok &= bench::run(CLIENT_NAME, "ExchangeInfo", 1, None, move || async move {
            black_box(client.exchange_information().await?.data().await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "AccountInfo") {
        let params = &AccountInformationV2Params::builder().build()?;

        is_ok &= bench::run(CLIENT_NAME, "AccountInfo", 1, None, move || async move {
            black_box(client.account_information_v2(params.clone()).await?.data().await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if bench::matches(filter, "LeverageBracket") {
        let params = &NotionalAndLeverageBracketsParams::builder().build()?;

        is_ok &= bench::run(CLIENT_NAME, "LeverageBracket", 1, None, move || async move {
            black_box(client.notional_and_leverage_brackets(params.clone()).await?.data().await?);
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
            black_box(client.new_order(params.clone()).await?.data().await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    Ok(is_ok)
}