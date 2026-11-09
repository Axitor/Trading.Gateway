using Axitor.Trading.Gateway.Core.Transport.WebSockets.Common.Models;

namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>What the dialect read from one stream message. Bounds are offsets into the message.</summary>
internal struct WsStreamInbound
{
    /// <summary>Id of the control request a reply answers. Zero for data and events.</summary>
    public long RequestId;

    /// <summary>Where the route key starts: the stream name, or what the dialect routes by.</summary>
    public int KeyStart;

    /// <summary>Length of the route key. Zero for an exchange event.</summary>
    public int KeyLength;

    /// <summary>Where the stream data starts. For an exchange event, the whole message.</summary>
    public int DataStart;

    /// <summary>Length of the stream data.</summary>
    public int DataLength;

    /// <summary>Exchange error code of a refused control request.</summary>
    public int ExchangeCode;

    /// <summary>What the route key means. Zero for a stream name; other values are the dialect's, e.g. an SBE template id.</summary>
    public int Channel;

    /// <summary>What the core must do after an exchange event, e.g. rotate the connection on a server shutdown.</summary>
    public WsInboundSignal Signal;

    /// <summary>Whether a control reply is an error.</summary>
    public bool IsError;
}
