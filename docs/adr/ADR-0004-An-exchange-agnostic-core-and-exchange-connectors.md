# ADR-0004: An exchange-agnostic core and exchange connectors

**Status:** Accepted
**Date:** 2026-11

## TL;DR

The library has two layers. The core does the hard, generic work every exchange needs: transports, reconnects, rate limit plumbing, network health, request correlation, stream routing. It doesn't know a single Binance URL, error code or JSON field. The Binance connector plugs into it through small static "dialect" interfaces that the compiler resolves ahead of time, so the split costs zero on the hot path. A new exchange is a new connector; the core, with all its resilience, is reused as it is.

## The problem

Every exchange speaks its own dialect. Binance signs requests one way, Bybit another. Error codes, rate limit headers, stream names, subscribe messages and handshake errors all differ. Yet the hard parts are the same everywhere: keep sockets alive, reconnect without losing data, match responses to requests, respect limits, notice a dead network.

Exchange clients usually go one of two ways:

- **One block of code per exchange.** Fast to start, but the resilience logic is rewritten, and re-debugged, for every exchange.
- **A shared base class with virtual methods.** Reuse is easy, but every exchange-specific step becomes a virtual call on the hot path, and the base class slowly fills with special cases.

I wanted the reuse of the second way with the speed of the first.

## Decision

### The core knows mechanics, not exchanges

The core (`Axitor.Trading.Gateway.Core`) owns:

- the transports: REST, WebSocket API and WebSocket streams;
- resilience: the ban gate, the network gate, the stream watchdog, reconnect policy, make-before-break rotation;
- request correlation for the WebSocket API, and the subscription registry and duplicate filter for streams;
- primitives every connector needs: `Symbol`, `ClientOrderId`, the fast decimal parser, the seqlock, the clock.

It has no idea what Binance is. A market is an opaque number to it, and a stream is an opaque key.

### The connector knows Binance

The connector (`Axitor.Trading.Gateway.Binance`) owns everything exchange-specific: signing, the rate limit windows, the error code tables, endpoints, parsers, models, sessions, ready-made handlers and the public `BinanceClient`.

### Dialects are static, not virtual

The core talks to the connector through dialect interfaces with `static abstract` members:

- `IExchangeDialectRestApi` reads rate limit headers and error bodies, and classifies error codes;
- `IExchangeDialectWsApi` tells a response from a pushed event and reads its id, writes the request envelope, reads the response envelope with its rate limits, and classifies error codes;
- `IExchangeDialectWsHandshake` classifies a rejected handshake and reads `Retry-After`;
- `IExchangeDialectWsStream` tells stream data from control replies, finds the route key, and writes subscribe messages.

The core is generic over the dialect (`where TDialect : struct, IExchangeDialectWsStream`). There is no dialect object and no vtable: each call is a static call to a known method, resolved when the JIT or the AOT compiler builds the code, and free to inline. Binance has four dialects: REST, WebSocket API, JSON streams and SBE streams.

### Routing without knowing the stream

On the receive loop the dialect reads one route key from each message: a channel and key bytes. For JSON streams that is the stream name; for SBE binary streams it is the template id and the symbol. The core looks the key up in its registry and hands the connector's dispatcher an `int` handle and the message bytes. The core never parses a price, and the connector never manages a socket.

### Markets are flags, logic is per market

Binance runs three markets: USD-M futures, COIN-M futures and spot. One `[Flags]` enum, `BinanceMarkets`, says both which market an endpoint belongs to and which markets a client turns on. Where markets differ, as with error codes, limits or stream formats, the dialect or the classifier switches on the market inside one method. The core only passes the number through.

### The API follows the exchange docs

The public tree mirrors the Binance documentation, so finding an endpoint takes no guessing:

    client.Futures.UsdM.RestApi.MarketData.KlinesData.GetAsync(symbol, interval, buffer)
    client.Futures.UsdM.WsApi.Trade.NewOrder.PlaceAsync(...)
    client.Futures.UsdM.WsStreams.BookTicker.CreateSession(handler)
    client.Spot.SbeStreams.BestBidAsk.CreateSession(handler)

## What it costs

- **Heavy generic signatures inside.** A REST call inside the core is generic over the payload, authenticator, limiter, parser, value and dialect. Users never see it, but contributors do. That is the price of a split with no runtime cost.
- **A market switch in cold code.** Error classification checks the market at run time. It only runs on failures, so it never touches the hot path.

## Alternatives I rejected

**Abstract base classes per exchange.** A virtual call on every message, and a base class that grows a special case for every exchange.

**Generic market types,** like a separate struct per market nested inside the dialect generics. It would remove a branch on a cold path and double the generic depth of every signature. Not worth it.

**A unified model across exchanges,** in the style of CCXT. It forces every exchange into the lowest common shape, drops exchange-specific fields and adds a mapping step to every message. Trading code needs the exchange's real data, fast.

## Related

ADR-0003, ADR-0005, ADR-0012, ADR-0017