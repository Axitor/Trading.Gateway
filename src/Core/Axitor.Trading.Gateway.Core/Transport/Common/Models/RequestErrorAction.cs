namespace Axitor.Trading.Gateway.Core.Transport.Common.Models;

/// <summary>What to do in case of a REST API failure.</summary>
public enum RequestErrorAction : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Means crashing the library and throwing an exception, the failure is unrecoverable without user intervention.</summary>
    Fatal,

    /// <summary>Retryable after a delay.</summary>
    Retry,

    /// <summary>Can be skipped.</summary>
    Skip,
}
