using System.Runtime.CompilerServices;

namespace Axitor.Trading.Gateway.Binance.Models.Depth;

/// <summary>Up to 20 order book levels stored inside the struct, with no array.</summary>
[InlineArray(20)]
public struct DepthLevels20
{
    private DepthLevel _element;
}
