using System.Runtime.CompilerServices;
using Axitor.Trading.Gateway.Binance.Models.Depth;

namespace Axitor.Trading.Gateway.Binance.Handlers;

/// <summary>One side of a local order book, sorted from worst to best, so the best level is last.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: Prices and quantities live in two arrays, and the binary search walks only the prices.
/// Ask prices are stored negated, so both sides sort the same way and share one code path. Negating a double is exact.
/// </para>
/// <para>
/// DESIGN NOTE: The boundary is the worst price the side knows exactly: beyond it levels are unknown and updates there are ignored.
/// It is the deepest snapshot level, or none when the snapshot had fewer levels than asked. A full side drops its worst level and moves the boundary in.
/// Changes come near the best price, at the end of the arrays, so inserts and removals there shift little.
/// </para>
/// </remarks>
internal sealed class OrderBookSide
{
    // Price, negated for asks.
    private readonly double[] _keys;
    private readonly double[] _quantities;
    private readonly double _sign;
    private double _boundary = double.NegativeInfinity;
    private int _count;

    public OrderBookSide(int capacity, bool isAsk)
    {
        _keys = new double[capacity];
        _quantities = new double[capacity];
        _sign = isAsk ? -1 : 1;
    }

    /// <summary>Whether a boundary is set but no level is left inside it: the best price is unknown.</summary>
    public bool IsBlind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count == 0 && HasBoundary;
    }

    private bool HasBoundary
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => double.NegativeInfinity < _boundary;
    }

    /// <summary>Whether fewer known levels than the threshold are left inside the boundary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool NeedsRefresh(int threshold) => _count < threshold && HasBoundary;

    public void Clear()
    {
        _count = 0;
        _boundary = double.NegativeInfinity;
    }

    /// <summary>Replaces the side with snapshot levels, best first. A full snapshot sets the boundary at its deepest level.</summary>
    public void Load(ReadOnlySpan<DepthLevel> levels, bool isFull)
    {
        int count = Math.Min(levels.Length, _keys.Length);

        for (int i = 0; i < count; i++)
        {
            int index = count - 1 - i;
            _keys[index] = levels[i].Price * _sign;
            _quantities[index] = levels[i].Quantity;
        }

        _count = count;
        _boundary = isFull && 0 < count ? _keys[0] : double.NegativeInfinity;
    }

    /// <summary>Sets the quantity at a price; zero removes the level. A price beyond the boundary is ignored.</summary>
    public void Apply(double price, double quantity)
    {
        double key = price * _sign;

        if (key < _boundary)
        {
            return;
        }

        int index = Find(key);

        if (0 <= index)
        {
            if (quantity == 0)
            {
                Remove(index);
            }
            else
            {
                _quantities[index] = quantity;
            }

            return;
        }

        // Removing a level the side does not hold is normal.
        if (quantity == 0)
        {
            return;
        }

        Insert(~index, key, quantity);
    }

    /// <summary>Copies levels best first and returns how many.</summary>
    public int CopyTop(Span<DepthLevel> destination)
    {
        // Read once: a write may be in progress, and the seqlock discards such a copy.
        int stored = _count;
        int count = Math.Min(stored, destination.Length);

        for (int i = 0; i < count; i++)
        {
            int index = stored - 1 - i;
            destination[i].Price = _keys[index] * _sign;
            destination[i].Quantity = _quantities[index];
        }

        return count;
    }

    /// <summary>The best level. False when the side is empty.</summary>
    public bool TryGetBest(out DepthLevel level)
    {
        int stored = _count;

        if (stored == 0)
        {
            level = default;
            return false;
        }

        level = new DepthLevel() { Price = _keys[stored - 1] * _sign, Quantity = _quantities[stored - 1] };
        return true;
    }

    /// <summary>Index of the key, or the bitwise complement of where it would go.</summary>
    private int Find(double key)
    {
        double[] keys = _keys;
        int low = 0;
        int high = _count - 1;

        while (low <= high)
        {
            int middle = (int)((uint)(low + high) >> 1);
            double current = keys[middle];

            if (current == key)
            {
                return middle;
            }

            if (current < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    private void Insert(int position, double key, double quantity)
    {
        Span<double> keys = _keys;
        Span<double> quantities = _quantities;

        if (_count < keys.Length)
        {
            keys[position.._count].CopyTo(keys[(position + 1)..]);
            quantities[position.._count].CopyTo(quantities[(position + 1)..]);
            keys[position] = key;
            quantities[position] = quantity;
            _count++;
            return;
        }

        // Full: the worst level goes and the boundary moves to the new worst.
        if (position == 0)
        {
            _boundary = keys[0];
            return;
        }

        keys[1..position].CopyTo(keys);
        quantities[1..position].CopyTo(quantities);
        keys[position - 1] = key;
        quantities[position - 1] = quantity;
        _boundary = keys[0];
    }

    private void Remove(int index)
    {
        Span<double> keys = _keys;
        Span<double> quantities = _quantities;

        keys[(index + 1).._count].CopyTo(keys[index..]);
        quantities[(index + 1).._count].CopyTo(quantities[index..]);
        _count--;
    }
}
