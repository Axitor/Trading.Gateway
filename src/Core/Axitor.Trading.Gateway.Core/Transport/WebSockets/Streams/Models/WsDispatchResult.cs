namespace Axitor.Trading.Gateway.Core.Transport.WebSockets.Streams.Models;

/// <summary>What became of one stream message handed to the dispatcher.</summary>
internal enum WsDispatchResult : byte
{
    /// <summary>Delivered to the handler, or dropped as a known duplicate.</summary>
    Handled,

    /// <summary>Could not be read and was dropped. Reported as an unknown message.</summary>
    Unreadable,

    /// <summary>Delivered, but a value in it was not recognized or did not fit. Reported to telemetry.</summary>
    NonStandard,

    /// <summary>A message that must not be lost could not be read. The transport stops for good.</summary>
    Fatal,
}
