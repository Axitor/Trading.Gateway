using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Axitor.Trading.Gateway.Tests.Fakes;

/// <summary>Stands in for the network under HttpClient: answers with the given responder and records every request.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private const string ApiKeyHeader = "X-MBX-APIKEY";

    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly ConcurrentQueue<FakeHttpRequest> _requests = new();

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    /// <summary>Every request so far, in the order they came.</summary>
    public IReadOnlyCollection<FakeHttpRequest> Requests => _requests;

    /// <summary>Requests whose path starts with the given one.</summary>
    public int Count(string pathPrefix)
    {
        int count = 0;

        foreach (FakeHttpRequest request in _requests)
        {
            if (request.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>A response with a JSON body and the given headers.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string body, params ReadOnlySpan<(string Name, string Value)> headers)
    {
        HttpResponseMessage response = new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        foreach ((string name, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    /// <summary>A failure the transport reads as a broken network.</summary>
    public static HttpRequestException NetworkFailure() =>
        new("Connection refused.", new SocketException((int)SocketError.ConnectionRefused));

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? apiKey = request.Headers.TryGetValues(ApiKeyHeader, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
        _requests.Enqueue(new FakeHttpRequest(request.Method, request.RequestUri!.PathAndQuery, apiKey));

        return await _respond(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>One request the fake handler saw.</summary>
internal readonly record struct FakeHttpRequest(HttpMethod Method, string PathAndQuery, string? ApiKey);
