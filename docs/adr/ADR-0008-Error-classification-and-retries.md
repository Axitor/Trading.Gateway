# ADR-0008: Error classification and retries you can trust with orders

**Status:** Accepted
**Date:** 2026-11

## TL;DR

Every failed request ends up in one of three actions: retry, skip or fatal. The rules come from a code-by-code reading of the Binance docs for all three markets, and where the docs disagree, the more careful reading wins. An answer the exchange can't vouch for is "unknown", never "failed". Nothing is retried that could double an order. Nothing that can't be fixed is retried at all. And every call returns a full breakdown of what happened, with no exceptions except for a real stop.

## The problem

An exchange answers a failed request in many ways: an HTTP status, an exchange error code, a dropped connection, a timeout, a body that is cut off. They mean very different things for the money:

- **"I didn't do it"** is safe to retry.
- **"I don't know if I did it"** must never be retried blindly for an order: that is how a position doubles.
- **"I won't ever do it"**, such as a bad signature, a revoked key or an account in liquidation, can't be fixed by retrying. Retrying a ban makes the ban longer.

The docs don't make this easy. The spot docs say any 5xx means the execution status is unknown. The futures docs say a 503 can go either way, depending on the message. The same code number can mean different things on different markets. A generic "retry 3 times on any error" policy gets all of this wrong.

## Decision

### Three actions, one classifier

A static classifier, `BinanceErrorClassifier`, turns a status code and an exchange code into a failure kind, and the transport turns the kind into one of three actions:

- **Retry**: wait and send again, only if the endpoint is marked retryable;
- **Skip**: return the failure to the caller with full details;
- **Fatal**: throw, because the app can't trade safely any more.

The classifier is shared by REST and the WebSocket API. Codes are split by market where they differ, and every code in the source carries its name and message from the docs.

### The rules

| What happened | Examples | Action |
|---|---|---|
| Can't be fixed by retrying | 401, 404, 413–415; -1002 unauthorized, -1016 service shutting down, -1022 bad signature, -2014/-2015 bad key; USD-M -1099, -2017 keys locked, -2023 user in liquidation, -4056/-4057 | **Fatal** |
| Long ban | 403, 418 | **Fatal**, and the ban gate holds every request for the exchange's Retry-After |
| Short ban | 429 | The ban gate holds every request for Retry-After, then retry if retryable |
| The exchange didn't process it | -1001 disconnected, -1008 server busy | Retry if retryable |
| Unknown status | 408, any 5xx, -1000, -1006, -1007, a network failure or timeout | Retry only if retryable, otherwise returned as unknown |
| Clock rejection | -1021; USD-M -5028, COIN-M -4188 | **Skip**, never fatal |
| Duplicate client order id | USD-M -4116; spot -2010 "Duplicate order sent." | **Skip** as "already placed" |
| Spot cancel-replace half done | 409 | **Skip** as "partial", with the error data that says which half went through |
| Any other exchange error | insufficient margin, invalid price and so on | **Skip** with code and message |

Two rules decide the dangerous cases:

- **Any 5xx is unknown, on every market.** The futures docs allow a 503 to mean "not executed", but reading it that way once too often doubles an order. A fatal code still wins over the status: a -1016 on a 503 means the service is gone.
- **"Retryable" is the endpoint's call, and the user can change it.** Placing an order is not retryable by default; canceling, modifying and queries are. A retry of an unknown order status happens only when the user has said it is safe. (ADR-0009)

### Retries are bounded by time, not by count

There is no retry counter. Each call can carry a time to live, and retries stop when it runs out: the request returns with an explicit “dropped by TTL” result. The TTL is checked right before every send, after all waiting, so a stale request never goes out. Zero means no limit. A count means nothing in trading; a deadline does: an order that arrives 10 seconds late is a different order.

Delays follow the exchange first. A Retry-After header is obeyed exactly and never shortened. Otherwise the delay is exponential back-off with random jitter. Bans wait at the ban gate. Network failures mark the network gate down and wait for its repair loop (ADR-0010), so a dead network doesn't burn the TTL on useless attempts.

### Sign at the last moment

A request is signed, and its timestamp set, only when it is about to be sent: after the ban gate, the network gate and the rate limiter have let it through. Waiting for a window doesn't eat into the exchange's receive window, and a request dropped before sending never pays for signing.

### The clock is the host's job

The library uses the host clock for timestamps and does no time sync of its own. A clock rejection is skipped and reported to telemetry. One late packet on a busy VPS must not kill a bot, and a host whose clock really drifts shows up in telemetry at once.

### Results, not exceptions

Every call returns a result struct:

- the outcome;
- the value;
- the error: reason, HTTP status, exchange code, and optionally the message and raw error data;
- the retry story: action, retry count, last delay;
- the rate limit headers;
- a latency breakdown into total time, network time, parse time and time spent in resilience.

Failures are data, not control flow. The only exception the library throws on a request is the fatal one.

## What it costs

- **The caller must check the outcome.** A result can't be ignored as easily as an exception can't be missed. That is the price of no exceptions on the hot path.
- **Unknown stays unknown.** After a timeout on a non-retryable order, the library says "unknown" and stops. The strategy has to check the order. It's the only honest answer.
- **Some safe retries are given up.** Treating every 5xx as unknown means a non-retryable request isn't sent again even when the futures docs would allow it.
- **Error text is off by default.** Message and error data are not captured unless the user turns them on, so a failure costs no string allocations. For debugging, it's one flag.

## Alternatives I rejected

**Exceptions for every failure.** Slow, easy to swallow with a broad catch, and they hide the difference between "not done" and "unknown".

**Retry N times on any error.** It extends bans, doubles orders and retries a revoked key forever.

**Follow each market's docs to the letter.** Two markets, two readings of the same status. One careful rule for all is simpler and safer.

**Sync the clock with the exchange in the client.** Estimating the offset from round-trip time guesses wrong on uneven routes, and it hides a broken host clock instead of reporting it.

## Related

ADR-0002, ADR-0007, ADR-0009, ADR-0010, ADR-0011