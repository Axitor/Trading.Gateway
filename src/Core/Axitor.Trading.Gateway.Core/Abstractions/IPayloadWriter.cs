using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Describes what to write into the request buffer.</summary>
internal interface IPayloadWriter
{
    /// <summary>Writes the query parameters for this call.</summary>
    void Write(ref PayloadBuilder builder);
}
