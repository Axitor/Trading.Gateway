using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Engine;
using Axitor.Trading.Gateway.Core.Time;
using Axitor.Trading.Gateway.Core.Transport.Common.Models;
using Axitor.Trading.Gateway.Core.Transport.Common.Payload;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Exceptions;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Core.Transport.RestApi.Wire;

/// <summary>REST API transport.</summary>
internal sealed class RestApiTransport : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly int _payloadBufferBytes;
    private readonly int _errorBufferBytes;
    private readonly bool _captureErrorMessageString;
    private readonly bool _captureErrorDataString;
    private readonly bool _captureFailureRequestUri;

    /// <summary>Creates a transport.</summary>
    public RestApiTransport(EngineOptions options, string? apiKey, string apiKeyHeader, HttpMessageHandler? messageHandler)
    {
        HttpMessageHandler telemetry = messageHandler ?? CreateHandler(options);

        _httpClient = CreateHttpClient(options, telemetry);

        _payloadBufferBytes = options.RestApi.Buffer.PayloadBufferBytes;
        _errorBufferBytes = options.RestApi.Buffer.ErrorBufferBytes;
        _captureErrorMessageString = options.RestApi.Resiliency.CaptureErrorMessageString;
        _captureErrorDataString = options.RestApi.Resiliency.CaptureErrorDataString;
        _captureFailureRequestUri = options.RestApi.Resiliency.CaptureFailureRequestUri;

        if (apiKey != null) _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(apiKeyHeader, apiKey);
    }

    /// <summary>Creates a handler.</summary>
    private static SocketsHttpHandler CreateHandler(EngineOptions options) =>
        new()
        {
            MaxConnectionsPerServer = options.RestApi.Network.MaxConnections,
            ConnectTimeout = options.RestApi.Network.ConnectTimeout,
            ResponseDrainTimeout = options.RestApi.Network.ResponseDrainTimeout,
            PooledConnectionLifetime = options.RestApi.Network.ConnectionLifetime,
            PooledConnectionIdleTimeout = options.RestApi.Network.ConnectionIdleTimeout,

            // DESIGN NOTE: Removing allocs on hot paths rather than compress cold path data
            AutomaticDecompression = DecompressionMethods.None,

            // DESIGN NOTE: Explicitly disabled to bypass internal .NET middleware. 
            // Exchanges do not use cookies or redirects. Forcing these to false 
            // removes redundant conditional checks from the hot execution path.
            AllowAutoRedirect = false,
            UseCookies = false,
        };

    /// <summary>Creates a http client.</summary>
    private static HttpClient CreateHttpClient(EngineOptions options, HttpMessageHandler handler)
    {
        HttpClient client = new(handler, disposeHandler: true)
        {
            Timeout = options.RestApi.Network.RequestTimeout,
        };

        client.DefaultRequestHeaders.ExpectContinue = false;

        return client;
    }

    /// <summary>Sends a request and gets its result.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<RestApiRequestResult<TValue>> SendAsync<TPayload, TAuth, TLimiter, TParser, TValue, TDialect, TTelemetry>(
        RestApiEndpointDescriptor descriptor,
        TPayload payload,
        TAuth auth,
        TLimiter limiter,
        TParser parser,
        TTelemetry telemetry,
        CancellationToken cancellationToken)
        where TPayload : struct, IPayloadWriter
        where TAuth : struct, IRequestAuthenticator
        where TParser : struct, IResponseParser<TValue>
        where TDialect : struct, IExchangeDialectRestApi
        where TTelemetry : struct, ITelemetryHandler
        where TLimiter : struct, IRateLimiter
    {
        long startTimerTransportFull = SysTime.GetTimestamp();

        // Build request payload
        string requestUri = BuildRequestUri(in descriptor, in payload, in auth);

        RestApiRequestResultHeaders headers = default;
        long startTimerTransportNetworkOnly = SysTime.GetTimestamp();
        long ticksTransportNetworkOnly = 0;
        long endTicks = 0;
        int statusCode = 0;

        try
        {
            // Omitted dedicated body read timeout. Most exchange payloads are tiny (usually 1 TCP packet), 
            // so the risk of a silent mid-read hang is negligible. Introducing a zero-alloc CTS pool 
            // for this specific edge case would add unnecessary complexity to the hot path.

            using HttpRequestMessage request = new(descriptor.HttpMethod, requestUri);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            ticksTransportNetworkOnly = SysTime.GetTimestamp() - startTimerTransportNetworkOnly;
            statusCode = (int)response.StatusCode;
            TDialect.ObserveHeaders<TLimiter>(limiter, response, descriptor.Market, ref headers);

            // Hot path
            if (statusCode == 200)
            {
                TValue value = await parser.ParseAsync(body, cancellationToken).ConfigureAwait(false);

                endTicks = SysTime.GetTimestamp();

                telemetry.OnRestApiRequestSucceeded(requestUri);

                return new RestApiRequestResult<TValue>()
                {
                    Outcome = RequestOutcome.Success,
                    Value = value,
                    Headers = headers,
                    Latency = new()
                    {
                        TransportFull = SysTime.NanosFromTicks(endTicks - startTimerTransportFull),
                        TransportNetworkOnly = SysTime.NanosFromTicks(ticksTransportNetworkOnly),
                        TransportParsingOnly = SysTime.NanosFromTicks(endTicks - startTimerTransportNetworkOnly - ticksTransportNetworkOnly),
                    }
                };
            }

            // Cold path, for status and api errors
            return await FailureExchangeStatusAsync<TValue, TDialect, TTelemetry>(response, body, headers, telemetry, requestUri, statusCode, descriptor.Market, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
        {
            // HttpClient.Timeout fired on this attempt, not caller cancellation.
            return Failure<TValue, TTelemetry>(RestApiRequestErrorReason.ExceptionTimeout, in headers, in telemetry, requestUri);
        }
        catch (HttpRequestException exception)
        {
            // Two independent diagnostics: SocketError for nested-socket breaks, HttpRequestError for the rest (TLS handshake, DNS, protocol).
            SocketError socketError = RestApiNetworkExceptions.TryGetSocketError(exception, out SocketError se) ? se : default;
            return Failure<TValue, TTelemetry>((socketError == default ? RestApiRequestErrorReason.ExceptionHttp : RestApiRequestErrorReason.ExceptionSocket), in headers, in telemetry, requestUri, statusCode, exception.HttpRequestError, in socketError);
        }
        catch (IOException)
        {
            // Response stream cut mid-body.
            return Failure<TValue, TTelemetry>(RestApiRequestErrorReason.ExceptionIO, in headers, in telemetry, requestUri, statusCode);
        }
        catch (NullReferenceException)
        {
            // Deliberately omit the 'response.Content == null' check to eliminate branching overhead.
            // The .NET SocketsHttpHandler strictly guarantees a non-null Response and its Content (yielding EmptyContent for empty bodies).
            // If a faulty test mock or a runtime glitch yields null, the resulting NullReferenceException will be safely caught and handled.
            return Failure<TValue, TTelemetry>(RestApiRequestErrorReason.ExceptionNull, in headers, in telemetry, requestUri, statusCode);
        }
        catch (JsonException)
        {
            // Exchanges strictly guarantee well-formed JSON, and typical trading responses are extremely short.
            // Pre-validating the payload validity via 'ResponsePeekBytes' before parsing just wastes CPU cycles.
            // Any structural corruption (e.g., body truncated by a network drop) will naturally caught and handled here.
            return Failure<TValue, TTelemetry>(RestApiRequestErrorReason.ExceptionJson, in headers, in telemetry, requestUri, statusCode);
        }
    }

    // <summary>Failure of a request.</summary>
    private RestApiRequestResult<TValue> Failure<TValue, TTelemetry>(
        RestApiRequestErrorReason reason,
        in RestApiRequestResultHeaders headers,
        in TTelemetry telemetry,
        string requestUri,
        int statusCode = 0,
        HttpRequestError httpError = default,
        in SocketError socketError = default,
        int exchangeCode = 0,
        string? exchangeMessage = null,
        string? exchangeData = null
        )
        where TTelemetry : struct, ITelemetryHandler
    {
        RestApiRequestResultError errors = new()
        {
            Reason = reason,
            StatusCode = statusCode,
            HttpError = httpError,
            SocketError = socketError,
            ExchangeCode = exchangeCode,
            ExchangeMessage = exchangeMessage,
            ExchangeData = exchangeData
        };

        if (_captureFailureRequestUri) errors.RequestUri = requestUri;

        telemetry.OnRestApiRequestFailed(requestUri, in errors);

        return new RestApiRequestResult<TValue>()
        {
            Outcome = RequestOutcome.Failed,
            Headers = headers,
            Errors = errors,
        };
    }
    /// <summary>Failure on non-200 status code or on an exchange error.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<RestApiRequestResult<TValue>> FailureExchangeStatusAsync<TValue, TDialect, TTelemetry>(
        HttpResponseMessage response,
        Stream streamBody,
        RestApiRequestResultHeaders headers,
        TTelemetry telemetry,
        string requestUri,
        int statusCode,
        int market,
        CancellationToken cancellationToken)
        where TDialect : struct, IExchangeDialectRestApi
        where TTelemetry : struct, ITelemetryHandler
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_errorBufferBytes);

        try
        {
            int length = await ReadToBufferAsync(streamBody, buffer, cancellationToken).ConfigureAwait(false);
            ReadOnlySpan<byte> spanBody = buffer.AsSpan(0, length);

            TDialect.ReadRestApiError(spanBody, statusCode, market, _captureErrorMessageString, _captureErrorDataString, out RestApiRequestErrorReason reason, out int exchangeCode, out string? exchangeMessage, out string? exchangeData);

            return Failure<TValue, TTelemetry>(reason, in headers, in telemetry, requestUri, statusCode, default, default, exchangeCode, exchangeMessage, exchangeData);

        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Stream to buffer.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private static async ValueTask<int> ReadToBufferAsync(Stream streamBody, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;

        while (total < buffer.Length)
        {
            int read = await streamBody.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>Builds the request line. The rented buffer and the ref struct builder both live and die inside this method.</summary>
    private string BuildRequestUri<TPayload, TAuth>(
        in RestApiEndpointDescriptor descriptor,
        in TPayload payload,
        in TAuth auth)
        where TPayload : struct, IPayloadWriter
        where TAuth : struct, IRequestAuthenticator
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_payloadBufferBytes);

        try
        {
            PayloadBuilder builder = new(buffer);
            payload.Write(ref builder);

            // Adds a timestamp exactly before sending a request
            if (descriptor.NeedsTimestamp)
            {
                auth.AppendTimestamp(ref builder, SysTime.UtcMillis());
            }

            if (descriptor.NeedsSignature)
            {
                auth.AppendSignature(ref builder);
            }

            // Needs a string alloc for a HttpRequestMessage
            return Encoding.UTF8.GetString(builder.AsSpan());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Dispose IDisposable resources.</summary>
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
