# ADR-0009: Orders that can't be placed twice by accident

**Status:** Accepted
**Date:** 2026-11

## TL;DR

An order timeout is the most expensive moment in trading code: the order may or may not be on the book, and a careless resend doubles the position. Every order here carries a client order id the user owns, and the compiler won't build an order without one. Placing is never retried unless the user says so. Every order is rounded and checked against the symbol rules before it is sent, so a bad order costs neither a network round trip nor the order rate limit. And the library never guesses an order's fate behind the strategy's back.

## The problem

The strategy sends an order and the network hiccups. The answer is a timeout, a 5xx or a dropped connection. Now there are two bad options:

- **Resend it.** If the first one went through, the bot now holds twice the position it wanted.
- **Don't resend.** If the first one never arrived, the bot thinks it's in a trade it isn't in.

Most clients make this worse in quiet ways:

- **They generate the client order id inside the library.** After a crash, the strategy doesn't know which id to look for.
- **They retry everything the same way,** orders included.
- **They send whatever they get.** A price off the tick or a quantity below the minimum goes to the exchange and comes back as a rejection: a full round trip, and a hit on the order rate limit, for an order that could never pass.

And Binance has a trap of its own. The exchange checks a client order id only against open orders. If the first order filled fast, it's no longer open, and a resend with the same id is accepted as a brand new order.

## Decision

### The client order id is required and belongs to the user

Every order request takes a `ClientOrderId`, and the field is a C# `required` member: forgetting it is a compile error, not a runtime surprise. The id is the user's. The strategy can store it before sending and look the order up by it after a crash or a timeout. It's a packed 36-byte value with the allowed characters checked once, so every order and every order event carries it with zero allocations. (ADR-0003)

### Placing is not retried unless the user says so

Each call has an `isRetryable` flag. Placing an order and placing an algo order default to `false`. Canceling and modifying default to `true`: doing them twice is harmless. The docs on every order method spell out the open-orders trap, and say what to do when unsure: leave retries off, and resend only after checking on the exchange that the order doesn't exist. All order calls default to Critical priority, so they take their own connection slots. (ADR-0007)

### "Duplicate" means "already placed"

If a resend hits an order that is still open, the exchange rejects it as a duplicate: -4116 on USD-M, -2010 with "Duplicate order sent." on spot. The library reports that as `OrderAlreadyPlaced`, not as an error. That is the answer the strategy actually needs: the first attempt reached the exchange. (ADR-0008)

### Every order is checked before it leaves

The user passes the symbol rules (`SymbolFilters`, filled once per symbol from exchange info) with every order. Before the rate limiter is even asked, the order is prepared:

- **Price goes to the nearest tick,** counted from the minimum price.
- **Quantity goes down to the step,** using the market step for market orders, so an order is never larger than asked.
- **Limits are checked:** minimum and maximum price, minimum and maximum quantity, and minimum notional. Like the exchange, the notional check is skipped for reduce-only and close-position orders, and when there is no price.
- **Comparisons allow for double rounding.** A price like 0.30000000000000004 must not fail a limit of 0.3, and 2.9999999999999996 steps counts as three.

An order that breaks a rule is not sent and not charged to any window. It comes back with `OrderFiltersViolated`, the exact rule it broke, and a dedicated telemetry event. Modify takes an explicit `isReduceOnly` flag for the same reason: the notional check depends on it, and only the user knows.

A test order endpoint is there too: the exchange validates the order and places nothing.

### The library doesn't reconcile behind the strategy's back

After an unknown outcome, the library returns "unknown" and stops. It doesn't query the exchange by order id on its own. The strategy knows what it intended, which ids it used and how much risk it can take while it checks; the library doesn't. The library also doesn't fetch symbol rules on its own: no hidden exchange info request at startup, and the user decides when rules are refreshed.

## What it costs

- **The user manages ids and symbol rules.** One more field per order, and one exchange info call per refresh. In return the ids survive a crash and the rules never change behind the strategy's back.
- **No automatic placement retries.** A transient failure on a new order comes back to the strategy instead of being hidden. That's the point.
- **Reconciliation is the strategy's job.** It's also the only place where it can be done right.

## Alternatives I rejected

**Library-generated client order ids.** Convenient until the first crash, when nobody knows which id to look for.

**Retry orders like any other request.** The open-orders trap turns a fast fill plus a retry into a second position.

**Let the exchange reject bad orders.** A round trip and an order limit hit for an answer the client could give in nanoseconds.

**Reconcile automatically with an order query.** It spends weight in the middle of an outage, the answer can be stale, and it makes decisions that belong to the strategy.

**Fetch exchange info automatically.** A large hidden request, and a symbol rules cache that changes under the strategy's feet.

## Related

ADR-0002, ADR-0003, ADR-0007, ADR-0008, ADR-0011