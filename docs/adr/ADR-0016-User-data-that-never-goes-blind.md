# ADR-0016: User data that never goes blind

**Status:** Accepted
**Date:** 2026-11

## TL;DR

User data is the bot's view of its own account: fills, orders, positions, balances. Lose one event and the bot trades on a position it doesn't have. Binance's user data streams have traps that no doc spells out: a dead key that still answers "subscribed", an expiry event that never comes, a stream that goes silent with the socket wide open. I mapped those traps with live probes and built the sessions around them. One connection per account keeps events in order. The key is re-checked after every reconnect. Every gap is reported once, with its real start. And no event is ever dropped without a trace.

## The problem

On futures, user data comes over a stream tied to a listen key that must be kept alive. On spot, the account is subscribed over a WebSocket API connection. Both look simple. Both break quietly:

- **A dead key looks alive.** A SUBSCRIBE with an expired listen key still answers success. No data ever comes, and nothing says why.
- **The expiry warning can vanish.** With an event filter, Binance sends the `listenKeyExpired` event only if you asked for it. If the key expires while the connection is down, the event is lost with the connection.
- **One key, many apps.** A listen key is shared by every app on the account. Closing it to "clean up" stops everyone else's user data too.
- **Order matters.** A fill and the position change it caused are separate events. If they arrive on different connections, they can arrive in the wrong order.
- **A rotation must not drop events.** Spot user data rides a WebSocket API connection that also gets replaced: daily, after a network repair, and on server shutdown notices.
- **New fields and new values appear.** Binance adds enum values and fields over time. A strict parser that throws loses the event; a lax one may hand the bot garbage.

## Decision

### Futures: one connection, one subscription per event, one anchor

- **One connection per session.** Every event type arrives on the same socket, so the exchange's order is kept: a fill always comes before the position change it caused.
- **Each event is its own subscription,** `listenKey@ORDER_TRADE_UPDATE`, `listenKey@ACCOUNT_UPDATE` and so on. The user picks events with a `[Flags]` value, and that same value is the exchange-side filter, so nothing is received only to be thrown away. A bare listen key subscription is never used: next to the per-event ones, every event would come twice.
- **`listenKey@listenKeyExpired` is always subscribed.** It is the session's anchor. Without it, an expired key would leave the socket open and silent, with no signal at all.
- **The key is checked after every reconnect** with `userDataStream.start`, not ping. On a live key it returns the same key and extends it; on a dead one it returns a new key. One call does both, and a different key means the old one died while the connection was down.
- **The key is never closed.** It belongs to the account, not to this app.
- **Keep-alive every 30 minutes** by default, well inside the 60-minute expiry.
- **One session serves USD-M and COIN-M.** COIN-M documents only the bare listen key, but it accepts the same per-event subscriptions; I checked it live.

### Spot: a dedicated WebSocket API connection

Spot user data uses signature subscriptions on a WebSocket API connection that belongs to the session alone.

- **A connection hook** subscribes the account on every new connection before it starts serving. On a rotation the new connection is subscribed before the old one drains, so no event falls between them. One account can be subscribed on two connections at once; I checked that live, because the design depends on it.
- **The connection is ours alone,** so "already subscribed" (-2035) and "not subscribed" (-2036) are read as success, and no subscription id needs to be tracked.
- **An unexpected `eventStreamTerminated`** opens a gap and subscribes again. It counts only if it comes from the connection that holds the subscription, and outside our own unsubscribe.
- **`serverShutdown`** starts a make-before-break rotation, the same as on the shared connections.

### Subscribe means "confirmed"

On both markets, subscribe returns only `Subscribed` or `Rejected`. Without an answer, the exchange may or may not hold the subscription, and a bot that loads its open orders after such a "maybe" would miss events with no signal. So the session keeps asking until the exchange answers. A canceled subscribe is rolled back: nothing of it stays registered, and no event reaches the handler.

### Gaps are reported once, with their real start

Only the anchor's breaks reach the handler: one `OnDisconnected` and one `OnReconnected` per gap. `OnReconnected` comes only after the key is checked, and it carries the gap window. The docs on it say what to do: events inside the gap are lost, so check open orders, positions, balances and trades for that window.

### No event lost without a trace

- **An unknown enum value** reads as `UNKNOWN`. A client order id that doesn't fit the packed type reads as empty. The event is still delivered, and telemetry reports the oddity.
- **A required field that can't be read** means the exchange changed its format, and the session stops with a fatal error. A lost fill leaves the position wrong; a bot must not keep trading on it.
- **Copies from a rotation overlap** are found by their bytes, and dropped. User data has no growing update id, and the spot envelope's subscription id differs between connections, so the event bytes are the only reliable key.

### Fast, even here

Events are parsed with `Utf8JsonReader`, with no tree and no reflection. Account and margin call updates come as a header plus spans of flat structs from reused buffers, with no array per event. The rare `listenKeyExpired`, which Binance writes differently from every other event, is read on a slow path, out of the way.

## What it costs

- **One connection per user data session.** Ordering is worth a socket.
- **A key check after every reconnect.** One cheap request, so the bot is never subscribed to a dead key.
- **Subscribe can take longer.** It waits for a real answer, because "maybe subscribed" is worse than "not yet".
- **A fatal stop on a format change.** Harsh, and right: a bot that can't read its own fills must not trade.

## Alternatives I rejected

**A bare listen key subscription.** No exchange-side filter, and no way to make the expiry event reliable.

**Ping to keep the key alive.** It can't tell a live key from a dead one; `start` does both jobs in one call.

**Close the key on shutdown.** It stops user data for every other app on the account.

**A connection per event type.** Fills and position updates could arrive out of order.

**Throw on any unknown value.** One new enum value from Binance would stop every bot running the library.

## Related

ADR-0002, ADR-0010, ADR-0011, ADR-0012