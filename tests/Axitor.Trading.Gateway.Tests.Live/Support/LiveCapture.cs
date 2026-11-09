using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using Axitor.Trading.Gateway.Binance.Configuration;

namespace Axitor.Trading.Gateway.Tests.Live.Support;

/// <summary>Spot SBE frames read with a plain socket, past the connector. Their hex goes to the test output for unit test fixtures.</summary>
internal static class LiveCapture
{
    // Message header: blockLength uint16, then templateId uint16.
    private const int TemplateIdOffset = 2;
    private const int ReceiveBufferBytes = 1 << 16;

    /// <summary>Subscribes the streams on one raw connection and returns the first binary frame of each template id. Fails after the wait limit.</summary>
    public static async Task<Dictionary<int, byte[]>> FirstFramesAsync(string apiKey, string[] streams, int[] templateIds)
    {
        CancellationToken testToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        limit.CancelAfter(LiveEnvironment.WaitLimit);

        using ClientWebSocket socket = new();
        socket.Options.SetRequestHeader(new BinanceSecret().ApiKeyHeader, apiKey);

        Dictionary<int, byte[]> frames = new(templateIds.Length);
        byte[] buffer = new byte[ReceiveBufferBytes];

        try
        {
            await socket.ConnectAsync(new BinanceUri().SpotSbeStream, limit.Token);
            await socket.SendAsync(SubscribeRequest(streams), WebSocketMessageType.Text, endOfMessage: true, limit.Token);

            while (frames.Count < templateIds.Length)
            {
                (byte[] message, WebSocketMessageType type) = await ReceiveAsync(socket, buffer, limit.Token);

                if (type == WebSocketMessageType.Close)
                {
                    Assert.Fail($"The exchange closed the connection: {socket.CloseStatus} {socket.CloseStatusDescription}");
                }

                // The subscribe reply comes as JSON in a text frame.
                if (type != WebSocketMessageType.Binary || message.Length < TemplateIdOffset + sizeof(ushort))
                {
                    continue;
                }

                int templateId = BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(TemplateIdOffset));

                if (0 <= Array.IndexOf(templateIds, templateId))
                {
                    frames.TryAdd(templateId, message);
                }
            }
        }
        catch (OperationCanceledException) when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Templates seen within 30 s: {string.Join(", ", frames.Keys)}.");
        }

        return frames;
    }

    /// <summary>Writes the frame as hex, ready for Convert.FromHexString in a fixture.</summary>
    public static void Report(LiveTelemetryLog log, int templateId, byte[] frame) =>
        log.Report($"SBE template {templateId}, {frame.Length} bytes: {Convert.ToHexString(frame)}");

    private static byte[] SubscribeRequest(string[] streams) =>
        Encoding.UTF8.GetBytes("{\"method\":\"SUBSCRIBE\",\"params\":[\"" + string.Join("\",\"", streams) + "\"],\"id\":1}");

    /// <summary>One whole message, however many frames it came in.</summary>
    private static async Task<(byte[] Message, WebSocketMessageType Type)> ReceiveAsync(ClientWebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using MemoryStream message = new();
        ValueWebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return (message.ToArray(), result.MessageType);
    }
}
