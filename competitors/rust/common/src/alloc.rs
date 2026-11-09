//! Global allocator that counts allocated bytes and calls: the Rust side of bytes per message.

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};

// Each thread counts on its own cache line, so counting adds no contention between the client's threads.
const SLOT_COUNT: usize = 128;

#[repr(align(128))]
struct Slot {
    bytes: AtomicU64,
    calls: AtomicU64,
}

static SLOTS: [Slot; SLOT_COUNT] = [const {
    Slot {
        bytes: AtomicU64::new(0),
        calls: AtomicU64::new(0),
    }
}; SLOT_COUNT];

static NEXT_SLOT: AtomicUsize = AtomicUsize::new(0);

thread_local! {
    // Const and without drop: native TLS, so reading it never allocates.
    static SLOT_INDEX: Cell<usize> = const { Cell::new(usize::MAX) };
}

/// System allocator that counts every allocation. Set it as the global allocator of a benchmark binary.
pub struct CountingAllocator;

unsafe impl GlobalAlloc for CountingAllocator {
    #[inline]
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        record(layout.size());
        unsafe { System.alloc(layout) }
    }

    #[inline]
    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        record(layout.size());
        unsafe { System.alloc_zeroed(layout) }
    }

    #[inline]
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        unsafe { System.dealloc(ptr, layout) }
    }

    /// Counts as a new block of the new size, as a grown array is counted in .NET.
    #[inline]
    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        record(new_size);
        unsafe { System.realloc(ptr, layout, new_size) }
    }
}

/// Bytes allocated by every thread so far.
pub fn allocated_bytes() -> u64 {
    SLOTS.iter().map(|slot| slot.bytes.load(Ordering::Relaxed)).sum()
}

/// Allocations by every thread so far.
pub fn allocation_count() -> u64 {
    SLOTS.iter().map(|slot| slot.calls.load(Ordering::Relaxed)).sum()
}

#[inline]
fn record(bytes: usize) {
    let slot = &SLOTS[slot_index()];
    slot.bytes.fetch_add(bytes as u64, Ordering::Relaxed);
    slot.calls.fetch_add(1, Ordering::Relaxed);
}

#[inline]
fn slot_index() -> usize {
    SLOT_INDEX
        .try_with(|index| {
            let mut value = index.get();

            if value == usize::MAX {
                value = NEXT_SLOT.fetch_add(1, Ordering::Relaxed) % SLOT_COUNT;
                index.set(value);
            }

            value
        })
        .unwrap_or(0)
}