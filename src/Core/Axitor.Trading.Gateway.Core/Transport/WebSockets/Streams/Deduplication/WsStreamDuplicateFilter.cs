using System.Numerics;
using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Deduplication;

/// <summary>Remembers the last messages and finds the copy another connection delivers. Not thread-safe: used under the shard dispatch lock.</summary>
/// <remarks>
/// DESIGN NOTE: Whole messages are compared, so a copy can be missed but a real message is never dropped.
/// Messages are first matched by a key, e.g. the event time, so most of them need no byte compare.
/// Slots grow for a bigger message and never shrink: any size is checked.
/// </remarks>
internal sealed class WsStreamDuplicateFilter
{
    private readonly Slot[] _slots;
    private int _next;

    /// <summary>Creates a filter of slotCount slots, each starting at slotBytes.</summary>
    public WsStreamDuplicateFilter(int slotCount, int slotBytes)
    {
        _slots = new Slot[slotCount];

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].Bytes = GC.AllocateUninitializedArray<byte>(slotBytes);
        }
    }

    /// <summary>True when the same message was seen before; otherwise remembers it.</summary>
    public bool IsDuplicate(long key, ReadOnlySpan<byte> message)
    {
        Slot[] slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            ref Slot slot = ref slots[i];

            // An empty slot has zero length and never matches a message.
            if (slot.Key == key && slot.Length == message.Length && slot.Bytes.AsSpan(0, message.Length).SequenceEqual(message))
            {
                return true;
            }
        }

        Remember(key, message);
        return false;
    }

    /// <summary>Forgets every message, e.g. before a new subscription.</summary>
    public void Clear()
    {
        Slot[] slots = _slots;

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Length = 0;
        }

        _next = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Remember(long key, ReadOnlySpan<byte> message)
    {
        ref Slot slot = ref _slots[_next];

        if (slot.Bytes.Length < message.Length)
        {
            slot.Bytes = Grow(message.Length);
        }

        message.CopyTo(slot.Bytes);
        slot.Key = key;
        slot.Length = message.Length;

        if (++_next == _slots.Length)
        {
            _next = 0;
        }
    }

    /// <summary>Cold path. A larger slot for a message bigger than any before.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Grow(int required)
    {
        uint rounded = BitOperations.RoundUpToPowerOf2((uint)required);
        return GC.AllocateUninitializedArray<byte>(rounded <= (uint)Array.MaxLength ? (int)rounded : required);
    }

    private struct Slot
    {
        public byte[] Bytes;
        public long Key;
        public int Length;
    }
}
