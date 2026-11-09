# ADR-0014: A local order book that never drifts silently

**Status:** Accepted
**Date:** 2026-11

## TL;DR

A local order book is only useful if it is exactly the exchange's book. A wrong book doesn't crash; it quietly shows prices that aren't there. My `OrderBookSet` follows Binance's sync procedure to the letter and checks every update against the one before it. It knows exactly how deep its knowledge goes, keeps serving while it refreshes, and never lets resyncs eat the weight that orders need. Readers on any thread get the top of the book without locks. At full load it processes 300k updates per second with zero garbage collections, 3.6x Binance.Net, with a p99.9 latency 7.8x lower.

## The problem

Binance publishes the book as a REST snapshot plus a stream of diff updates. Joining them correctly is fiddly, and every shortcut breaks the book:

- **Updates race the snapshot.** The stream starts before the snapshot arrives. Updates older than the snapshot must be dropped, and the first one applied must straddle the snapshot's id.
- **Every update must continue the previous one.** On futures each update carries the id of the one before it (`pu`). A missing update means the book is wrong from that point on.
- **The markets differ.** On spot the first update after a snapshot starts one id past it; on futures it doesn't.
- **A snapshot only covers so many levels.** Past its deepest level, the client doesn't know the book. Updates there must not be read as truth.
- **Resyncs cost weight.** A 1,000-level futures snapshot costs 20 weight. A reconnect that resyncs fifty books at once can eat the minute's budget, and then the cancel that should close a losing position waits.

A client that applies the first update after the snapshot without checking where it starts gets a wrong book whenever the stream runs ahead of the snapshot.

## Decision

### Sync by the exchange's procedure, checked on every update

While a snapshot loads, updates are buffered. Once it arrives:

- the boundary is the snapshot's `lastUpdateId` plus the market offset: 0 on futures, 1 on spot;
- buffered updates that end below the boundary are dropped;
- the first update applied must start at or below the boundary and end at or above it;
- every next update must continue the chain: its previous id must equal the last applied id.

Any break means a new snapshot. A message that can't be parsed is dropped, so the next update breaks the chain and the book is rebuilt. A wrong book is never patched up and served.

The buffer keeps one unbroken chain of parsed levels in flat arrays. A gap, or the 8 MB cap, starts it over from the current update.

### Each side knows how far its knowledge goes

The boundary of a side is the worst price it knows exactly: the deepest level of the snapshot, or none if the snapshot had fewer levels than asked. Updates beyond the boundary are ignored, because the book doesn't know what lies there. A full side drops its worst level and moves the boundary in.

As updates eat into the known depth, a side gets shallow. Below a threshold, a quarter of the snapshot limit by default, the book loads a fresh snapshot in the background **and keeps serving** while it loads. Its updates are buffered too, and the book is rebuilt once the snapshot fits. The strategy never sees a gap for a routine refresh.

### Snapshots wait their turn, and never starve orders

- **Two queues.** A book that is out of sync goes ahead of a book that is only shallow.
- **One snapshot in flight per book.** Each request carries a ticket. A reset or a newer request takes a new ticket, so a request left behind in a queue is skipped, not applied.
- **A weight budget of its own.** Snapshots are charged to the market's request weight and to a snapshot window capped at 50% of it by default. However many books resync at once, half of every minute stays free for orders and cancels. (ADR-0007)
- **Back-off on failure,** from 500 ms up to 30 s between tries.

### A layout built for the hot path

Each side keeps prices and quantities in two separate arrays, sorted from worst to best, so the best level is last. A binary search walks only the price array, which keeps it dense in cache. Changes cluster near the best price, at the end of the arrays, so inserts and removals there shift little. Ask prices are stored negated, so both sides sort the same way and share one code path; negating a double is exact.

### One writer at a time, readers never wait

Two writers touch a book: the receive loop and the snapshot worker. They take a per-book lock that nobody else holds on the hot path. Readers on any thread go through a seqlock (ADR-0015). `TryGetBest` and `TryCopyTop` copy the best levels without a lock and never see a half-applied update. Both return false while the book is out of sync, so a strategy can't act on a broken book.

### One set per market and session

An `OrderBookSet` is bound to one depth session of its own market. A mismatch, or a second session, fails at creation, not at run time. `ResyncCount` is public, so a test or a dashboard can prove the books stay in sync.

## Results

Order book set on the full depth stream, 20 symbols, JIT build:

| Benchmark | Axitor | Binance.Net |
|---|---:|---:|
| Max throughput, updates/s | **300k** | 82.4k (3.6x) |
| p50 / p99 / p99.9 at live pace, µs | **100 / 215 / 277** | 152 / 390 / 2,159 |
| GC collections per run | **0** | Gen0, Gen1 and Gen2, up to 72 ms of pauses |

No resync and no gap in any benchmark run. The live tests subscribe real Binance depth streams on all three markets and check `ResyncCount == 0`.

## What it costs

- **A lock per update, per book.** Uncontended on the hot path; the snapshot worker takes it only while it applies a snapshot.
- **Rotation can cost a resync.** While a connection is replaced, the new one may deliver an update before the old one delivers the update just before it. The chain check catches it and the book is rebuilt. That happens rarely: on the daily rotation and after a network repair.
- **Unknown depth stays unknown.** Past its boundary the book reports nothing. It doesn't guess from updates it can't verify.
- **Snapshots wait under load.** With the 50% cap, a resync storm takes longer to finish. Orders never wait for it.

## Alternatives I rejected

**Apply the first update after the snapshot without checking.** Works until the stream runs ahead of the snapshot, then serves a wrong book.

**Read the book under a reader-writer lock.** Readers would hold back the receive loop. A seqlock lets them retry instead.

**A sorted dictionary per side.** An allocation per new level and pointer chasing on every lookup. Two flat arrays are faster and allocate nothing.

**Trust levels beyond the snapshot.** An update deep in the book can't be checked against anything. Showing it as truth is how a book drifts.

## Related

ADR-0002, ADR-0007, ADR-0012, ADR-0013, ADR-0015