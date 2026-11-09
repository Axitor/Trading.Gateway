# ADR-0012: A stream transport that keeps up with the market

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Market data is where volume lives: a busy book ticker stream pushes hundreds of thousands of messages per second in a burst. My stream transport routes each message to its subscriber with one lookup in a lock-free table, hands it to the handler on the receive loop with no queue in between, and replaces connections make-before-break, so a rotation never opens a hole in the data. On one connection it keeps 47% of what a bare socket loop can read on book tickers and 53% on depth. Binance.Net keeps 12% and 7%, the official Rust client 5% and 3%.

## The problem

A stream client looks simple: open a socket, subscribe, read messages. At real volume it gets hard:

- **Routing costs per message.** Every message must find its subscriber. A dictionary keyed by a string means a string per message; a scan over subscriptions grows with their number.
- **Queues add latency and garbage.** Many clients read the socket on one thread and process on another, through a queue. Each message pays for a queue operation and a thread hop.
- **Connections don't live forever.** Binance closes stream connections after 24 hours and limits streams per connection. Reconnecting the plain way, close and then open, leaves a hole in the data every day.
- **Subscriptions change at run time.** A bot adds and removes symbols while data flows, and a handle must never be reused while old messages for it are still in flight.

## Decision

### Combined streams, subscribed at run time

Every connection uses the combined stream endpoint, and streams are added and removed with SUBSCRIBE and UNSUBSCRIBE messages while data flows. Connections open on demand: a shard connects when the first subscription lands on it. The defaults are 5 connections of 200 streams per route. Adding connections first keeps each receive loop light; packing more streams into one connection adds latency to all of them.

### A stream is registered before it is subscribed

The subscription gets its handle and its route key before the request goes out, so data that arrives right after the reply already finds its handler. A subscribe with no reply in time stays registered and comes back as `Unconfirmed`; the next reconnect subscribes it again. A stream is removed only after the exchange confirms the unsubscribe, so its handle can't be given to another stream while the old data still arrives.

### Routing with one lookup and no lock

On the receive loop, the dialect reads the route key from the message: the stream name for JSON, or the template id and symbol for SBE binary streams (ADR-0004, ADR-0017). The registry finds the handle in an open-addressing table that is never more than half full, so every search stops at an empty slot within a step or two. Subscription changes are rare: they take a lock, rebuild the table and swap it in whole. Receive loops read the current table with no lock and no allocation.

### The handler runs on the receive loop

There is no queue between the socket and the handler. The message is parsed and delivered right where it was read, and the handler's contract is simple: return fast and never throw. Heavy work goes to another thread through the ready-made `ChannelSink` handler, by the user's choice, not by default. (ADR-0005, ADR-0015)

### Rotation without a hole

Each connection is replaced before it gets old (23 hours by default), after every network repair (ADR-0010), and when spot announces a server shutdown. The replacement is make-before-break:

1. A new connection opens and subscribes every stream of the shard.
2. Once the exchange confirms, it becomes current, and the old connection closes.
3. While both are open, both deliver the same data, and the connector drops what it already handled: by update id for market data, by event time for klines, by comparing the bytes for user data.

If the new connection isn't confirmed, the old one keeps serving, and the rotation is tried again later.

### One dispatch lock per shard, on purpose

During a rotation, two receive loops deliver the same subscriptions, and the handler must see them one at a time and in order. So every data message is dispatched under a per-shard lock. Outside a rotation only one loop ever takes it: uncontended, it costs about two atomic operations, far less than receiving and decrypting the message.

A flag that turns the lock on only during rotation looks cheaper, but it isn't safe. The old loop can read the flag just before it is set and call the handler while the new loop already runs. Closing that gap needs an atomic step on every message, and that is exactly what the lock costs.

### Limits enforced on the connection

- **Outgoing messages** per connection are counted in a sliding one-second window, with room kept for the keep-alive ping and the pong to a server ping, so control traffic never crosses the exchange's message limit.
- **Incoming messages** are read whole into a buffer that grows up to a ceiling: 1 MB by default, the only buffer setting the user needs. A message over the ceiling closes the connection with `MessageTooBig`. Nothing is ever cut or dropped silently.
- **Sockets** are the BCL `ClientWebSocket`, read with the `ValueTask`-based receive into a reused buffer: no allocation per read.

## Results

Max throughput on one connection, 20 symbols, median of 5 runs, JIT build. Raw is a loop that only reads and drops messages on the same socket: the ceiling for any client.

| Stream | Raw | Axitor | Binance.Net | binance-sdk (Rust) | Nautilus (Rust) |
|---|---:|---:|---:|---:|---:|
| Book ticker | 2.43M | **1.14M (47%)** | 291k (12%) | 129k (5%) | 181k (7%) |
| Depth | 908k | **484k (53%)** | 62.2k (7%) | 30.1k (3%) | 46.7k (5%) |
| Depth 20 | 800k | **327k (41%)** | 39.2k (5%) | 19.6k (2%) | 30.6k (4%) |

Zero garbage collections on our side in every run.

## What it costs

- **Handlers must be fast.** A slow handler slows its connection. The contract is in the docs, and `ChannelSink` is one line away.
- **Two connections during a rotation.** For a short overlap, each rotated shard holds twice the connections and receives every message twice. The duplicates never reach the handler.
- **An uncontended lock per message.** Two atomic operations, for a guarantee that a rotation can never reorder or interleave the data.

## Alternatives I rejected

**A queue between the socket and the handler.** A queue operation and a thread hop on every message, and a place for garbage to pile up.

**Fixed stream sets in the URL.** No run-time subscribe, and every change of symbols means a new connection.

**Close-then-open reconnects for rotation.** A hole in the data every 24 hours per connection.

**A custom WebSocket framer on raw sockets.** No measurable gain over the internet, where TLS and the network dominate, and real risk. The BCL socket with an allocation-free receive is enough.

## Related

ADR-0004, ADR-0005, ADR-0010, ADR-0013, ADR-0015, ADR-0017