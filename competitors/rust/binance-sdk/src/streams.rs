//! binance-sdk market streams: typed subscriptions with a message callback per stream, as its examples use them.

use std::sync::Arc;

use anyhow::bail;
use axitor_bench_common::harness::{self, BoxError, STREAM_SYMBOLS, StreamClient, StreamKind};
use axitor_bench_common::latency::LatencyCollector;
use binance_sdk::config::ConfigurationWebsocketStreams;
use binance_sdk::derivatives_trading_usds_futures::DerivativesTradingUsdsFuturesWsStreams;
use binance_sdk::derivatives_trading_usds_futures::websocket_streams::{
    AggregateTradeStreamsParams, AggregateTradeStreamsResponse, DiffBookDepthStreamsParams,
    DiffBookDepthStreamsResponse, DiffBookDepthStreamsUpdateSpeedEnum, IndividualSymbolBookTickerStreamsParams,
    IndividualSymbolBookTickerStreamsResponse, KlineCandlestickStreamsIntervalEnum, KlineCandlestickStreamsParams,
    KlineCandlestickStreamsResponse, PartialBookDepthStreamsLevelsEnum, PartialBookDepthStreamsParams,
    PartialBookDepthStreamsResponse, PartialBookDepthStreamsUpdateSpeedEnum, WebsocketStreams,
};
use tokio::runtime::Runtime;

use crate::CLIENT_NAME;

// The SDK always reconnects; an hour keeps a drop from starting a second pass, so a drop shows as a short pass.
const RECONNECT_DELAY_MILLIS: u64 = 3_600_000;

/// Arguments after "streams". Returns the exit code.
pub fn run(args: &[String]) -> i32 {
    let runtime = match tokio::runtime::Builder::new_multi_thread().enable_all().build() {
        Ok(runtime) => runtime,
        Err(error) => {
            eprintln!("Tokio runtime: {error}");
            return 1;
        }
    };

    let mut client = BinanceSdkStreams { runtime };
    harness::run_streams(CLIENT_NAME, &mut client, args)
}

/// The SDK on a default multi-thread Tokio runtime; its connection tasks call the callbacks.
struct BinanceSdkStreams {
    runtime: Runtime,
}

impl StreamClient for BinanceSdkStreams {
    type Run = WebsocketStreams;

    fn supports(&self, kind: StreamKind) -> bool {
        kind != StreamKind::Book
    }

    // Its connection events go to subscribers as a copy of every message, which would load the measured path.
    fn tracks_gaps(&self) -> bool {
        false
    }

    fn start(&mut self, kind: StreamKind, collector: Arc<LatencyCollector>) -> Result<WebsocketStreams, BoxError> {
        self.runtime.block_on(connect(kind, collector)).map_err(Into::into)
    }

    fn stop(&mut self, run: WebsocketStreams) {
        if let Err(error) = self.runtime.block_on(run.disconnect()) {
            eprintln!("Disconnect: {error}");
        }
    }
}

/// Connects the SDK's market, public and private connections and subscribes every symbol.
async fn connect(kind: StreamKind, collector: Arc<LatencyCollector>) -> anyhow::Result<WebsocketStreams> {
    let configuration = ConfigurationWebsocketStreams::builder()
        .ws_url(format!("ws://{}", harness::stub_host()))
        .reconnect_delay(RECONNECT_DELAY_MILLIS)
        .build()?;

    let connection = DerivativesTradingUsdsFuturesWsStreams::from_config(configuration).connect().await?;

    for symbol in STREAM_SYMBOLS {
        subscribe(&connection, kind, symbol.to_ascii_lowercase(), Arc::clone(&collector)).await?;
    }

    Ok(connection)
}

async fn subscribe(connection: &WebsocketStreams, kind: StreamKind, symbol: String, collector: Arc<LatencyCollector>) -> anyhow::Result<()> {
    match kind {
        StreamKind::BookTicker => {
            let params = IndividualSymbolBookTickerStreamsParams::builder(symbol).build()?;

            connection
                .individual_symbol_book_ticker_streams(params)
                .await?
                .on_message(move |message: IndividualSymbolBookTickerStreamsResponse| {
                    collector.record(message.e_uppercase.unwrap_or(0));
                });
        }
        StreamKind::AggTrade => {
            let params = AggregateTradeStreamsParams::builder(symbol).build()?;

            connection
                .aggregate_trade_streams(params)
                .await?
                .on_message(move |message: AggregateTradeStreamsResponse| {
                    collector.record(message.e_uppercase.unwrap_or(0));
                });
        }
        StreamKind::Depth => {
            let params = DiffBookDepthStreamsParams::builder(symbol)
                .update_speed(DiffBookDepthStreamsUpdateSpeedEnum::UpdateSpeed100ms)
                .build()?;

            connection
                .diff_book_depth_streams(params)
                .await?
                .on_message(move |message: DiffBookDepthStreamsResponse| {
                    collector.record(message.e_uppercase.unwrap_or(0));
                });
        }
        StreamKind::Depth20 => {
            let params = PartialBookDepthStreamsParams::builder(symbol, PartialBookDepthStreamsLevelsEnum::Levels20)
                .update_speed(PartialBookDepthStreamsUpdateSpeedEnum::UpdateSpeed100ms)
                .build()?;

            connection
                .partial_book_depth_streams(params)
                .await?
                .on_message(move |message: PartialBookDepthStreamsResponse| {
                    collector.record(message.e_uppercase.unwrap_or(0));
                });
        }
        StreamKind::Kline => {
            let params = KlineCandlestickStreamsParams::builder(symbol, KlineCandlestickStreamsIntervalEnum::Interval1m).build()?;

            connection
                .kline_candlestick_streams(params)
                .await?
                .on_message(move |message: KlineCandlestickStreamsResponse| {
                    collector.record(message.e_uppercase.unwrap_or(0));
                });
        }
        StreamKind::Book => bail!("binance-sdk has no local order book."),
    }

    Ok(())
}