using System.Globalization;
using System.Net.Http.Headers;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;

/// <summary>Reads response headers without allocating.</summary>
internal static class RestApiHeaderReader
{
    /// <summary>Reads a non-negative integer header through the non-validated view.</summary>
    public static int ReadInt(HttpResponseMessage response, string name)
    {
        if (!response.Headers.NonValidated.TryGetValues(name, out HeaderStringValues values)) return 0;

        foreach (string value in values) return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

        return 0;
    }
}
