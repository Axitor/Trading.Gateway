using System.Numerics;
using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Subscriptions;

/// <summary>Subscriptions of one route: handles, the connection each one lives on, and the lookup by route key.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Changes are rare, so they take a lock. After each change the lookup table is rebuilt
/// and swapped in whole, so receive loops read it with no lock and no allocations.
/// </para>
/// <para>
/// DESIGN NOTE: The lookup table is never more than half full, so a search always stops at an empty slot.
/// Keys are hashed with HashCode; its seed is random per process, but stable within one, which is all we need.
/// </para>
/// <para>
/// DESIGN NOTE: Data is found by a route key: a channel and key bytes the dialect reads from the message.
/// For JSON streams the channel is zero and the key is the stream name. A binary protocol carries no name, e.g. SBE,
/// so its dialect routes by what the message has: the template id as the channel and the symbol as the key.
/// The stream name still goes into the subscribe and unsubscribe requests.
/// </para>
/// <para>
/// DESIGN NOTE: The transport removes a stream only after the exchange answers its unsubscribe request.
/// Until then the data still reaches its handle, and the handle cannot be reused too early.
/// </para>
/// <para>
/// DESIGN NOTE: A resubscribe may copy a name that is being unsubscribed at that moment. This is harmless:
/// the exchange subscribes it once more, and the unsubscribe request removes it right after.
/// </para>
/// <para>
/// DESIGN NOTE: A receive loop may find a handle just before it is removed or given to another stream.
/// Every subscription stores its name as a new array, so the dispatch checks under the shard lock that the handle still holds the array it found.
/// </para>
/// </remarks>
internal sealed class WsStreamRegistry
{
    private const int MinLookupSize = 16;

    private readonly Lock _sync = new();
    private readonly byte[]?[] _names;
    private readonly byte[]?[] _keys;
    private readonly int[] _channels;
    private readonly int[] _shardOf;
    private readonly int[] _memberIndex;
    private readonly int[][] _shardMembers;
    private readonly int[] _shardCounts;
    private readonly int[] _freeHandles;
    private readonly int _maxStreamsPerShard;
    private int _freeCount;
    private int _count;

    // Swapped in whole on every change; the receive loops read it without a lock.
    private Lookup _lookup;

    /// <summary>Creates a registry for up to maxShards × maxStreamsPerShard subscriptions.</summary>
    public WsStreamRegistry(int maxShards, int maxStreamsPerShard)
    {
        int capacity = maxShards * maxStreamsPerShard;

        _names = new byte[]?[capacity];
        _keys = new byte[]?[capacity];
        _channels = new int[capacity];
        _shardOf = new int[capacity];
        _memberIndex = new int[capacity];
        _shardMembers = new int[maxShards][];
        _shardCounts = new int[maxShards];
        _freeHandles = new int[capacity];
        _maxStreamsPerShard = maxStreamsPerShard;

        for (int i = 0; i < maxShards; i++)
        {
            _shardMembers[i] = new int[maxStreamsPerShard];
        }

        // Lowest handles are handed out first.
        for (int i = 0; i < capacity; i++)
        {
            _freeHandles[i] = capacity - 1 - i;
        }

        _freeCount = capacity;
        _lookup = BuildLookup();
    }

    /// <summary>Most subscriptions the route can hold. Handles are below this value.</summary>
    public int Capacity => _names.Length;

    /// <summary>
    /// Cold path. Registers a stream on the first connection with room.
    /// False when its route key is already registered (handle is the existing one) or every connection is full (handle is -1).
    /// </summary>
    public bool TryAdd(ReadOnlySpan<byte> name, int channel, ReadOnlySpan<byte> key, out int handle, out int shard)
    {
        lock (_sync)
        {
            handle = FindLocked(channel, key);

            if (0 <= handle)
            {
                shard = _shardOf[handle];
                return false;
            }

            shard = FindShardWithRoom();

            if (shard < 0)
            {
                return false;
            }

            handle = _freeHandles[--_freeCount];
            byte[] storedName = name.ToArray();
            _names[handle] = storedName;

            // A stream routed by its name shares the array.
            _keys[handle] = key.SequenceEqual(name) ? storedName : key.ToArray();
            _channels[handle] = channel;
            _shardOf[handle] = shard;

            int position = _shardCounts[shard]++;
            _shardMembers[shard][position] = handle;
            _memberIndex[handle] = position;
            _count++;

            Volatile.Write(ref _lookup, BuildLookup());
            return true;
        }
    }

    /// <summary>Cold path. Removes a subscription and frees its handle. False when the handle is not registered.</summary>
    public bool TryRemove(int handle)
    {
        lock (_sync)
        {
            if ((uint)_names.Length <= (uint)handle || _names[handle] is null)
            {
                return false;
            }

            int shard = _shardOf[handle];
            int position = _memberIndex[handle];
            int last = --_shardCounts[shard];

            // Swap-remove: the last member takes the freed position.
            int moved = _shardMembers[shard][last];
            _shardMembers[shard][position] = moved;
            _memberIndex[moved] = position;

            _names[handle] = null;
            _keys[handle] = null;
            _freeHandles[_freeCount++] = handle;
            _count--;

            Volatile.Write(ref _lookup, BuildLookup());
            return true;
        }
    }

    /// <summary>Cold path. The name and connection of a registered handle. False when the handle is not registered.</summary>
    public bool TryGet(int handle, out byte[] name, out int shard)
    {
        lock (_sync)
        {
            if ((uint)_names.Length <= (uint)handle || _names[handle] is not byte[] registered)
            {
                name = [];
                shard = -1;
                return false;
            }

            name = registered;
            shard = _shardOf[handle];
            return true;
        }
    }

    /// <summary>Cold path. Copies the handles and names on a connection into buffers of maxStreamsPerShard. Returns how many.</summary>
    public int CopyShard(int shard, int[] handles, byte[][] names)
    {
        lock (_sync)
        {
            int count = _shardCounts[shard];
            int[] members = _shardMembers[shard];

            for (int i = 0; i < count; i++)
            {
                int handle = members[i];
                handles[i] = handle;
                names[i] = _names[handle]!;
            }

            return count;
        }
    }

    /// <summary>
    /// Handle of a stream by its route key, or -1 when it is not registered. The registered name it returns tells IsCurrent
    /// which subscription the handle had. Runs on the receive loop.
    /// </summary>
    public int Find(int channel, ReadOnlySpan<byte> key, out byte[]? registered)
    {
        Lookup lookup = Volatile.Read(ref _lookup);
        int[] handles = lookup.Handles;
        int mask = lookup.Mask;
        int hash = Hash(channel, key);

        // The table is at most half full, so the probe always reaches an empty slot.
        for (int index = hash & mask; ; index = (index + 1) & mask)
        {
            int handle = handles[index];

            if (handle < 0)
            {
                registered = null;
                return -1;
            }

            if (lookup.Hashes[index] == hash && lookup.Channels[index] == channel && key.SequenceEqual(lookup.Keys[index]))
            {
                registered = lookup.Names[index];
                return handle;
            }
        }
    }

    /// <summary>Whether the handle still holds the subscription Find saw: false once it was removed or given to another stream. Lock-free.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsCurrent(int handle, byte[] registered) => ReferenceEquals(Volatile.Read(ref _names[handle]), registered);

    /// <summary>Handle of a stream by its route key, or -1. Called under the lock.</summary>
    private int FindLocked(int channel, ReadOnlySpan<byte> key) => Find(channel, key, out _);

    private int FindShardWithRoom()
    {
        for (int shard = 0; shard < _shardCounts.Length; shard++)
        {
            if (_shardCounts[shard] < _maxStreamsPerShard)
            {
                return shard;
            }
        }

        return -1;
    }

    /// <summary>Builds the lookup from the current route keys. Called under the lock.</summary>
    private Lookup BuildLookup()
    {
        int size = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(MinLookupSize, _count * 2));
        int mask = size - 1;

        int[] handles = new int[size];
        int[] hashes = new int[size];
        int[] channels = new int[size];
        byte[][] keys = new byte[size][];
        byte[][] names = new byte[size][];
        Array.Fill(handles, -1);

        for (int handle = 0; handle < _names.Length; handle++)
        {
            if (_names[handle] is not byte[] name)
            {
                continue;
            }

            byte[] key = _keys[handle]!;
            int channel = _channels[handle];
            int hash = Hash(channel, key);
            int index = hash & mask;

            while (0 <= handles[index])
            {
                index = (index + 1) & mask;
            }

            handles[index] = handle;
            hashes[index] = hash;
            channels[index] = channel;
            keys[index] = key;
            names[index] = name;
        }

        return new Lookup(handles, hashes, channels, keys, names, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(int channel, ReadOnlySpan<byte> key)
    {
        HashCode hash = default;
        hash.Add(channel);
        hash.AddBytes(key);
        return hash.ToHashCode();
    }

    /// <summary>Open-addressing table by route key hash. Never changed once published.</summary>
    private sealed class Lookup(int[] handles, int[] hashes, int[] channels, byte[][] keys, byte[][] names, int mask)
    {
        public readonly int[] Handles = handles;
        public readonly int[] Hashes = hashes;
        public readonly int[] Channels = channels;
        public readonly byte[][] Keys = keys;
        public readonly byte[][] Names = names;
        public readonly int Mask = mask;
    }
}
