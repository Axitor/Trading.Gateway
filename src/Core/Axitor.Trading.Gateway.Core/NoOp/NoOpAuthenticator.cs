using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.NoOp;

/// <summary>No Op Authenticator for public endpoints.</summary>
internal readonly struct NoOpAuthenticator : IRequestAuthenticator
{
    /// <inheritdoc/>
    public void AppendTimestamp(ref PayloadBuilder builder, long timestampMillis) { }

    /// <inheritdoc/>
    public void AppendSignature(ref PayloadBuilder builder) { }
}
