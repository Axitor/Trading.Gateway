namespace Axitor.Trading.Gateway.Core.Transport.Common.Models;

/// <summary>How a REST API call ended.</summary>
public enum RequestOutcome : byte
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>Failed with an error.</summary>
    Failed,

    /// <summary>Was canceled (it does not mean it was not send at all, could be canceled on the way).</summary>
    Canceled,

    /// <summary>Succeeded.</summary>
    Success,

}
