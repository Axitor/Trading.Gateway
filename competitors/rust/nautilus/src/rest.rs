//! Nautilus REST calls through its raw futures HTTP client, the same requests as the .NET REST benchmarks.
//!
//! The client always applies the Binance limits itself: 2400 requests a minute, orders 300 per 10 s and 1200 a minute,
//! shared by every client in the process. Each benchmark stays inside one burst, and the next one waits for the refill.
//! It has no leverage bracket call, so that benchmark is left out.

use std::hint::black_box;
use std::time::Duration;

use axitor_bench_common::harness::{self, BoxError};
use axitor_bench_common::{bench, report};
use nautilus_binance::common::enums::{
    BinanceEnvironment, BinanceFuturesOrderType, BinanceProductType, BinanceSide, BinanceTimeInForce,
};
use nautilus_binance::futures::http::query::{BinanceKlinesParams, BinanceNewOrderParamsBuilder};
use nautilus_binance::futures::http::{BinanceDepthParams, BinanceFuturesUsdExchangeInfo, BinanceRawFuturesHttpClient};
use nautilus_common::live::get_runtime;

use crate::{API_KEY, API_SECRET, CLIENT_NAME, CLIENT_ORDER_ID, SYMBOL};

const ORDER_BOOK_LIMIT: u32 = 1000;

// Calls per benchmark, under the burst of the request limit and of the 10 s order limit.
const REQUEST_BUDGET: u32 = 2300;
const ORDER_BUDGET: u32 = 290;

// A full refill of the minute window, with a second to spare.
const REFILL: Duration = Duration::from_secs(61);

/// Arguments after "rest": an optional part of the benchmark names. Returns the exit code.
pub fn run(args: &[String]) -> i32 {
    match get_runtime().block_on(run_suite(args.first().map(String::as_str))) {
        Ok(true) => 0,
        Ok(false) => 1,
        Err(error) => {
            eprintln!("{error}");
            1
        }
    }
}

async fn run_suite(filter: Option<&str>) -> Result<bool, BoxError> {
    let client = BinanceRawFuturesHttpClient::new(
        BinanceProductType::UsdM,
        BinanceEnvironment::Live,
        Some(API_KEY.to_string()),
        Some(API_SECRET.to_string()),
        Some(format!("http://{}", harness::stub_host())),
        None,
        None,
        None,
    )?;

    let client = &client;
    let mut suite = Suite { filter, is_first: true, is_ok: true };

    println!("{}", report::REQUEST_HEADER);
    println!("{}", report::REQUEST_SEPARATOR);

    for count in [500_u32, 1500] {
        let name = format!("Klines({count})");

        if suite.next(&name).await {
            let params = &BinanceKlinesParams {
                symbol: SYMBOL.to_string(),
                interval: "1m".to_string(),
                start_time: None,
                end_time: None,
                limit: Some(count),
            };

            suite.is_ok &= bench::run(CLIENT_NAME, &name, 1, Some(REQUEST_BUDGET), move || async move {
                black_box(client.klines(params).await?);
                Ok::<(), BoxError>(())
            })
            .await;
        }
    }

    if suite.next("OrderBook").await {
        let params = &BinanceDepthParams {
            symbol: SYMBOL.to_string(),
            limit: Some(ORDER_BOOK_LIMIT),
        };

        suite.is_ok &= bench::run(CLIENT_NAME, "OrderBook", 1, Some(REQUEST_BUDGET), move || async move {
            black_box(client.depth(params).await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if suite.next("ExchangeInfo").await {
        // The same request and model its exchange_info call reads, without filling the instrument cache.
        suite.is_ok &= bench::run(CLIENT_NAME, "ExchangeInfo", 1, Some(REQUEST_BUDGET), move || async move {
            black_box(client.get::<(), BinanceFuturesUsdExchangeInfo>("exchangeInfo", None, false, false).await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if suite.next("AccountInfo").await {
        suite.is_ok &= bench::run(CLIENT_NAME, "AccountInfo", 1, Some(REQUEST_BUDGET), move || async move {
            black_box(client.query_account().await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    if suite.next("NewOrder").await {
        let params = &BinanceNewOrderParamsBuilder::default()
            .symbol(SYMBOL)
            .side(BinanceSide::Buy)
            .order_type(BinanceFuturesOrderType::Limit)
            .time_in_force(BinanceTimeInForce::Gtc)
            .quantity("0.01")
            .price("60000")
            .new_client_order_id(CLIENT_ORDER_ID)
            .build()
            // Nautilus builds its builders without std, so the error has Display only.
            .map_err(|error| error.to_string())?;

        suite.is_ok &= bench::run(CLIENT_NAME, "NewOrder", 1, Some(ORDER_BUDGET), move || async move {
            black_box(client.submit_order(params).await?);
            Ok::<(), BoxError>(())
        })
        .await;
    }

    Ok(suite.is_ok)
}

struct Suite<'a> {
    filter: Option<&'a str>,
    is_first: bool,
    is_ok: bool,
}

impl Suite<'_> {
    /// Whether the benchmark runs; before every one but the first, waits for the rate limiter to refill.
    async fn next(&mut self, name: &str) -> bool {
        if !bench::matches(self.filter, name) {
            return false;
        }

        if !self.is_first {
            eprintln!("Waiting {} s for the client's rate limiter to refill.", REFILL.as_secs());
            tokio::time::sleep(REFILL).await;
        }

        self.is_first = false;
        true
    }
}