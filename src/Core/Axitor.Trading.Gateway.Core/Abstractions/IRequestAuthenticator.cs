using Axitor.Trading.Gateway.Core.Transport.Common.Payload;

namespace Axitor.Trading.Gateway.Core.Abstractions;

/// <summary>Appends exchange authentications material to a request payload.</summary>
internal interface IRequestAuthenticator
{
    /// <summary>Appends the timestamp parameter.</summary>
    public void AppendTimestamp(ref PayloadBuilder builder, long timestampMillis);

    /// <summary>Signs the query string and appends the signature.</summary>
    public void AppendSignature(ref PayloadBuilder builder);
}
