# ADR-0011: A WebSocket API built for orders

**Status:** Accepted
**Date:** 2026-11

## TL;DR

The WebSocket API is the fast way to send orders: no new HTTP request per call, one open connection, a response in about the time of a network round trip. My client puts a request on the wire with a pooled, allocation-free await, matches the response by id in a fixed table, and parses it on the caller's thread so one big answer never delays another. Orders get their own connection. Every request ends with an outcome that says exactly whether it could have reached the exchange. Against Binance.Net, the client's own cost per order is 2.9x lower, with 34x less memory.

## The problem

Over a WebSocket, a request and its response are two separate messages on a shared connection. That creates problems REST doesn't have:

- **Correlation.** Responses come back in any order. Each one must find its waiting caller by id, fast and without allocating.
- **Head-of-line blocking.** If the receive loop parses every response, one large account answer delays every order response behind it.
- **Mixed traffic.** Account queries and market data share the socket with orders. A burst of queries must never push an order back.
- **An honest outcome.** A request can fail before it is sent, while it waits, or after it is sent. For an order those are very different things: "never sent" is safe to resend, "sent, no answer" is not.

## Decision

### Two lanes: Critical and Normal

Each enabled market gets two connections: a Critical lane for orders and cancels, and a Normal lane for everything else. While the Critical lane reconnects, critical requests may use the Normal lane. Normal requests never take the Critical one. A lane with no open connection waits for one, within the request's time to live, instead of failing at once.

### A fixed table of pending requests

Each lane has a table of request slots, 256 by default, sized to a power of two. A request takes a fresh id from an atomic counter, and the id picks its slot with a bit mask. If that slot is still busy, the id is skipped and the next one is tried; skipped ids never reach the wire. If every slot is busy, the request is rejected at once with `InFlightLimit`. 256 requests in flight with no answers means the network or the exchange is lagging badly, and in fast trading a fast "no" is safer than a queue.

### An await with no allocation

Each slot implements `IValueTaskSource` on top of `ManualResetValueTaskSourceCore`. Awaiting a response returns a `ValueTask` backed by the slot itself: no `Task`, no `TaskCompletionSource`, no allocation per request. The slot is reused for the next request once both sides, the caller and the receive loop, have let it go. The public endpoint methods use `PoolingAsyncValueTaskMethodBuilder`, so their async state machines are pooled too.

### The receive loop only routes

On the receive loop, the dialect reads just the response id. The loop copies the message into the slot's own buffer and wakes the caller; the caller's continuation always runs off the receive loop. Parsing the envelope, the result and the rate limits happens on the caller's thread. A 1 MB account answer never delays the order response behind it.

### Signed on the stack

Each signed request is signed on its own with HMAC. The builder writes the params in name order, as the signature requires, into stack buffers. It hashes them and hex-encodes the signature on the stack too. Nothing is allocated, and the API key and secret never end up in a string.

### Every outcome says whether the order could exist

A slot remembers when its frame was handed to the socket and when the answer arrived. A request ends in exactly one outcome:

| Outcome | Reached the exchange? |
|---|---|
| Response | Yes, and here is the answer |
| NotSent, NoConnection, InFlightLimit | No. Safe to resend |
| Timeout, ConnectionLost | Maybe. Check before resending an order |
| AppShutdown | The app is stopping |

A background sweeper checks deadlines every 250 ms; the response timeout is 5 seconds by default. A lost connection ends every request sent on it, and only those: a slot remembers which connection carried it.

### Recovery without dropping orders

Connections are replaced make-before-break: after every network repair (ADR-0010), and before the exchange's 24-hour connection limit with a 23-hour default age. The new connection takes new requests at once. The old one keeps serving its requests in flight until each has an answer or a timeout, then closes.

## Results

Client cost over a raw WebSocket loop on the same stub, JIT build:

| Call | Axitor | Binance.Net | binance-sdk (Rust) |
|---|---:|---:|---:|
| Ticker price | **14 µs** | 45 µs | 26 µs |
| New order | **24 µs** | 69 µs | 73 µs |
| Account balance | **35 µs** | 81 µs | 119 µs |
| Account info | **1.37 ms** | 1.94 ms | 11.9 ms |

Memory per call: 294–394 B for ticker, new order and cancel, against 8.8–13.2 KB for Binance.Net and 13–24 KB for the Rust client. That is less than a bare socket loop allocates per call in the same harness. Under a burst of 100 requests in flight: 1.16 KB per request against 13.5 KB.

## What it costs

- **A fixed in-flight limit.** At 256 pending requests a lane says no instead of queuing. It's a setting, and hitting it is a signal, not a bottleneck.
- **A slot buffer per request.** Each response is copied once into its slot, which costs one copy of the bytes. In return the receive loop never parses, and never waits on a slow caller.
- **Two connections per market,** even when the bot sends few orders. Orders never queue behind queries.
- **The caller must handle "maybe".** Timeout and ConnectionLost can't be turned into yes or no by the library. (ADR-0009)

## Alternatives I rejected

**A `TaskCompletionSource` per request.** Simple, and it allocates on every call.

**A concurrent dictionary of pending requests.** It allocates on insert, and it hides the in-flight limit that should be explicit.

**Parsing on the receive loop.** One heavy response delays every response behind it, orders included.

**One connection for everything.** A burst of account queries pushes orders back.

## Related

ADR-0005, ADR-0007, ADR-0008, ADR-0009, ADR-0010, ADR-0016