//! Nautilus market streams: its pooled futures stream client, read from its output stream of Binance structs.
//! The data client turns these into Nautilus types afterwards; that step is not part of the run.

use std::sync::Arc;

use axitor_bench_common::harness::{self, BoxError, STREAM_SYMBOLS, StreamClient, StreamKind};
use axitor_bench_common::latency::LatencyCollector;
use futures_util::StreamExt;
use nautilus_binance::common::enums::{BinanceEnvironment, BinanceProductType};
use nautilus_binance::futures::websocket::streams::messages::BinanceFuturesWsStreamsMessage;
use nautilus_binance::futures::websocket::BinanceFuturesWebSocketClient;
use nautilus_common::live::get_runtime;
use nautilus_network::websocket::TransportBackend;
use tokio::task::JoinHandle;

use crate::CLIENT_NAME;

/// Arguments after "streams". Returns the exit code.
pub fn run(args: &[String]) -> i32 {
    harness::run_streams(CLIENT_NAME, &mut NautilusStreams, args)
}

/// The client on the Nautilus runtime, where its own tasks run.
struct NautilusStreams;

struct Running {
    client: BinanceFuturesWebSocketClient,
    reader: JoinHandle<()>,
}

impl StreamClient for NautilusStreams {
    type Run = Running;

    // The local order book lives in the Nautilus data engine, above this client.
    fn supports(&self, kind: StreamKind) -> bool {
        kind != StreamKind::Book
    }

    fn tracks_gaps(&self) -> bool {
        true
    }

    fn start(&mut self, kind: StreamKind, collector: Arc<LatencyCollector>) -> Result<Running, BoxError> {
        get_runtime().block_on(connect(kind, collector))
    }

    fn stop(&mut self, run: Running) {
        let Running { mut client, reader } = run;

        get_runtime().block_on(async move {
            if let Err(error) = client.close().await {
                eprintln!("Close: {error}");
            }

            if let Err(error) = reader.await {
                eprintln!("Reader: {error}");
            }
        });
    }
}

async fn connect(kind: StreamKind, collector: Arc<LatencyCollector>) -> Result<Running, BoxError> {
    let (route, suffix) = match kind {
        StreamKind::BookTicker => ("public", "@bookTicker"),
        StreamKind::AggTrade => ("market", "@aggTrade"),
        StreamKind::Depth => ("public", "@depth@100ms"),
        StreamKind::Depth20 => ("public", "@depth20@100ms"),
        StreamKind::Kline => ("market", "@kline_1m"),
        StreamKind::Book => return Err("Nautilus has no local order book at this level.".into()),
    };

    // Raw routes, as Nautilus connects live: the data comes without the combined wrapper.
    let url = format!("ws://{}/{route}/ws", harness::stub_host());

    let mut client = BinanceFuturesWebSocketClient::new(
        BinanceProductType::UsdM,
        BinanceEnvironment::Live,
        None,
        None,
        Some(url),
        None,
        TransportBackend::default(),
    )?;

    client.connect().await?;

    // Taken before subscribing, so no message waits unread.
    let messages = client.stream();
    let reader = tokio::spawn(read(messages, collector));

    let streams = STREAM_SYMBOLS.iter().map(|symbol| format!("{}{suffix}", symbol.to_ascii_lowercase())).collect();
    client.subscribe(streams).await?;

    Ok(Running { client, reader })
}

/// Feeds every data message to the collector until the client closes its output.
async fn read(messages: impl futures_util::Stream<Item = BinanceFuturesWsStreamsMessage>, collector: Arc<LatencyCollector>) {
    let mut messages = std::pin::pin!(messages);

    while let Some(message) = messages.next().await {
        match message {
            BinanceFuturesWsStreamsMessage::BookTicker(data) => collector.record(data.event_time),
            BinanceFuturesWsStreamsMessage::AggTrade(data) => collector.record(data.event_time),
            BinanceFuturesWsStreamsMessage::DepthUpdate(data) => collector.record(data.event_time),
            BinanceFuturesWsStreamsMessage::Kline(data) => collector.record(data.event_time),
            BinanceFuturesWsStreamsMessage::Reconnected(_) => collector.add_gap(),
            _ => {}
        }
    }
}