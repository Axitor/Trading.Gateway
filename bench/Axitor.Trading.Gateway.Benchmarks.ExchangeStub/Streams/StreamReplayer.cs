using System.Buffers.Text;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Streams;

/// <summary>Replays recorded stream frames to each connection: subscriptions, filtering by stream, pace, event time rewrite, combined or raw mode.</summary>
/// <remarks>
/// <para>
/// The event time of every frame is replaced by the send time on the machine-wide monotonic clock, in microseconds.
/// A client handler reads latency as now minus event time on the same clock, whatever language it is written in.
/// </para>
/// <para>
/// A connection gets one pass of the recordings, then stays silent. A second pass would restart the update ids,
/// and clients that drop older ids, ours included, would drop it whole.
/// </para>
/// <para>
/// The pass starts once subscriptions stop changing for a moment, so a client that subscribes one stream at a time gets all of them from the start.
/// </para>
/// <para>
/// Diff depth of a symbol starts at the update that covers its snapshot, the first one the exchange docs say to apply.
/// The recorder took the snapshots 15 to 20 s into the streams; live, a client never sees that much older data.
/// </para>
/// <para>
/// At max pace the frames go out in 64 KB chunks: one socket write per frame would make the stub the bottleneck. At a set pace every frame goes out alone.
/// </para>
/// </remarks>
internal sealed class StreamReplayer
{
    private const int SettleMillis = 500;
    private const int MinDelayMicros = 2000;
    private const int MaxEventTimeDigits = 20;

    private static readonly double _microsPerTick = 1_000_000.0 / Stopwatch.Frequency;

    private readonly FrameRecording[] _recordings;
    private readonly bool[][] _skips;
    private readonly Dictionary<string, int> _streamIds;
    private readonly double _pace;

    private StreamReplayer(FrameRecording[] recordings, bool[][] skips, Dictionary<string, int> streamIds, double pace)
    {
        _recordings = recordings;
        _skips = skips;
        _streamIds = streamIds;
        _pace = pace;
    }

    private static ReadOnlySpan<byte> FinalUpdateIdKey => "\"u\":"u8;

    /// <summary>Reads every frame recording in the folder. Pace 1 is the recorded speed, 0 as fast as the client reads.</summary>
    public static StreamReplayer Load(string dir, double pace, IReadOnlyDictionary<string, long> snapshotUpdateIds)
    {
        // Task.Delay on Windows ticks every 15.6 ms by default; 1 ms keeps the recorded pace smooth.
        if (OperatingSystem.IsWindows())
        {
            _ = TimeBeginPeriod(1);
        }

        Dictionary<string, int> streamIds = [];
        List<FrameRecording> recordings = [];

        foreach (string path in Directory.GetFiles(dir, "*.axrec"))
        {
            FrameRecording recording = FrameRecording.Load(path, streamIds);
            recordings.Add(recording);
            Console.WriteLine($"Streams: {recording.Name}, {recording.Count} frames.");
        }

        string?[] depthSymbols = FindDiffDepthSymbols(streamIds);
        bool[][] skips = new bool[recordings.Count][];
        int skipped = 0;

        for (int r = 0; r < recordings.Count; r++)
        {
            skips[r] = FindSkips(recordings[r], depthSymbols, snapshotUpdateIds, out int recordingSkipped);
            skipped += recordingSkipped;
        }

        Console.WriteLine($"Streams: {streamIds.Count} stream names, {skipped} diff depth frames older than their snapshot left out.");
        return new StreamReplayer([.. recordings], skips, streamIds, pace);
    }

    /// <summary>Serves one connection until the client leaves. Streams may come in the path, the query or SUBSCRIBE messages.</summary>
    public async Task RunAsync(WebSocket socket, CoalescingStream? transport, string path, string streamsQuery, CancellationToken cancellationToken)
    {
        bool isRaw = !path.Contains("/stream", StringComparison.Ordinal);
        Connection connection = new(_streamIds.Count, isRaw);

        Subscribe(connection, streamsQuery.Split('/', StringSplitOptions.RemoveEmptyEntries));

        int rawStreams = path.IndexOf("/ws/", StringComparison.Ordinal);

        if (0 <= rawStreams)
        {
            Subscribe(connection, path[(rawStreams + 4)..].Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        Console.WriteLine($"Client on {path} ({(isRaw ? "raw" : "combined")}).");

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task send = SendAsync(socket, transport, connection, stop.Token);

        await ReceiveAsync(socket, connection, stop.Token).ConfigureAwait(false);

        await stop.CancelAsync().ConfigureAwait(false);
        await send.ConfigureAwait(false);
    }

    /// <summary>Stream id to uppercase symbol for diff depth streams, e.g. btcusdt@depth@100ms; null for every other stream.</summary>
    private static string?[] FindDiffDepthSymbols(Dictionary<string, int> streamIds)
    {
        string?[] symbols = new string?[streamIds.Count];

        foreach (KeyValuePair<string, int> pair in streamIds)
        {
            string name = pair.Key;
            int at = name.IndexOf('@', StringComparison.Ordinal);

            if (at <= 0)
            {
                continue;
            }

            ReadOnlySpan<char> kind = name.AsSpan(at + 1);

            if (kind.SequenceEqual("depth") || kind.StartsWith("depth@", StringComparison.Ordinal))
            {
                symbols[pair.Value] = name[..at].ToUpperInvariant();
            }
        }

        return symbols;
    }

    /// <summary>Marks diff depth frames whose final update id is below the snapshot of their symbol.</summary>
    private static bool[] FindSkips(FrameRecording recording, string?[] depthSymbols, IReadOnlyDictionary<string, long> snapshotUpdateIds, out int skipped)
    {
        bool[] skips = new bool[recording.Count];
        skipped = 0;

        for (int i = 0; i < recording.Count; i++)
        {
            int stream = recording.StreamIds[i];

            if (stream < 0 || depthSymbols[stream] is not { } symbol || !snapshotUpdateIds.TryGetValue(symbol, out long snapshotUpdateId))
            {
                continue;
            }

            ReadOnlySpan<byte> data = recording.Blob.AsSpan(recording.Offsets[i] + recording.DataStarts[i], recording.DataLengths[i]);
            int key = data.IndexOf(FinalUpdateIdKey);

            if (0 <= key
                && Utf8Parser.TryParse(data[(key + FinalUpdateIdKey.Length)..], out long finalUpdateId, out _)
                && finalUpdateId < snapshotUpdateId)
            {
                skips[i] = true;
                skipped++;
            }
        }

        return skips;
    }

    private async Task ReceiveAsync(WebSocket socket, Connection connection, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];

        try
        {
            while (true)
            {
                int length = 0;

                while (true)
                {
                    if (length == buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }

                    ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    length += result.Count;

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                byte[]? reply = HandleControl(connection, buffer.AsSpan(0, length));

                if (reply is null)
                {
                    continue;
                }

                await connection.SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await socket.SendAsync(reply, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    connection.SendLock.Release();
                }
            }
        }
        catch (WebSocketException)
        {
            // The client went away.
        }
        catch (OperationCanceledException)
        {
            // The stub is stopping.
        }
    }

    /// <summary>Applies SUBSCRIBE and UNSUBSCRIBE and builds the reply. Null for anything else. Fields may come in any order.</summary>
    private byte[]? HandleControl(Connection connection, ReadOnlySpan<byte> message)
    {
        string? method = null;
        List<string> names = [];
        ReadOnlySpan<byte> id = "null"u8;

        try
        {
            Utf8JsonReader reader = new(message);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("method"u8))
                {
                    reader.Read();
                    method = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
                else if (reader.ValueTextEquals("params"u8))
                {
                    reader.Read();

                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            names.Add(reader.GetString()!);
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    int start = (int)reader.TokenStartIndex;
                    int length = reader.TokenType == JsonTokenType.String ? reader.ValueSpan.Length + 2 : reader.ValueSpan.Length;
                    id = message.Slice(start, length);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        if (method == "SUBSCRIBE")
        {
            Subscribe(connection, names);
        }
        else if (method == "UNSUBSCRIBE")
        {
            Unsubscribe(connection, names);
        }
        else
        {
            return null;
        }

        return [.. "{\"result\":null,\"id\":"u8, .. id, .. "}"u8];
    }

    private void Subscribe(Connection connection, IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            // A stream with no recording is accepted and stays silent, as on the exchange.
            if (_streamIds.TryGetValue(name, out int id) && !connection.Subscribed[id])
            {
                connection.Subscribed[id] = true;
                Interlocked.Increment(ref connection.SubscribedCount);
            }
        }

        Volatile.Write(ref connection.LastChangeTicks, Stopwatch.GetTimestamp());
    }

    private void Unsubscribe(Connection connection, IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            if (_streamIds.TryGetValue(name, out int id) && connection.Subscribed[id])
            {
                connection.Subscribed[id] = false;
                Interlocked.Decrement(ref connection.SubscribedCount);
            }
        }

        Volatile.Write(ref connection.LastChangeTicks, Stopwatch.GetTimestamp());
    }

    private async Task SendAsync(WebSocket socket, CoalescingStream? transport, Connection connection, CancellationToken cancellationToken)
    {
        try
        {
            await WaitForSubscriptionsAsync(connection, cancellationToken).ConfigureAwait(false);

            bool isCoalescing = _pace == 0 && transport is not null;

            if (isCoalescing)
            {
                transport!.IsCoalescing = true;
            }

            int[] cursors = new int[_recordings.Length];
            long passStart = Stopwatch.GetTimestamp();
            long firstMicros = -1;
            long sent = 0;

            while (true)
            {
                int next = NextRecording(cursors);

                if (next < 0)
                {
                    break;
                }

                FrameRecording recording = _recordings[next];
                int frame = cursors[next]++;
                int stream = recording.StreamIds[frame];

                if (stream < 0 || !connection.Subscribed[stream] || _skips[next][frame])
                {
                    continue;
                }

                // The pace counts from the first frame this connection gets, so a stream that starts late in the recording does not start with a silence.
                if (firstMicros < 0)
                {
                    firstMicros = recording.Micros[frame];
                }

                if (0 < _pace)
                {
                    long waitMicros = (long)((recording.Micros[frame] - firstMicros) / _pace) - ElapsedMicros(passStart);

                    if (MinDelayMicros < waitMicros)
                    {
                        await Task.Delay((int)(waitMicros / 1000) - 1, cancellationToken).ConfigureAwait(false);
                    }
                }

                int length = BuildFrame(recording, frame, connection);

                await connection.SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await socket.SendAsync(connection.Buffer.AsMemory(0, length), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    connection.SendLock.Release();
                }

                sent++;
            }

            if (isCoalescing)
            {
                transport!.IsCoalescing = false;
                await transport.DrainAsync(cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine($"Pass done: {sent} frames in {Stopwatch.GetElapsedTime(passStart).TotalSeconds:F1} s.");
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // The client went away.
        }
        catch (IOException)
        {
            // The client went away while a chunk was going out.
        }
        catch (OperationCanceledException)
        {
            // The client went away or the stub is stopping.
        }
    }

    private static async Task WaitForSubscriptionsAsync(Connection connection, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(SettleMillis, cancellationToken).ConfigureAwait(false);

            bool hasStreams = 0 < Volatile.Read(ref connection.SubscribedCount);
            bool isSettled = SettleMillis <= Stopwatch.GetElapsedTime(Volatile.Read(ref connection.LastChangeTicks)).TotalMilliseconds;

            if (hasStreams && isSettled)
            {
                return;
            }
        }
    }

    /// <summary>The recording whose next frame is the earliest, or -1 when every one is done.</summary>
    private int NextRecording(int[] cursors)
    {
        int best = -1;
        long bestMicros = long.MaxValue;

        for (int r = 0; r < _recordings.Length; r++)
        {
            FrameRecording recording = _recordings[r];
            int cursor = cursors[r];

            if (cursor < recording.Count && recording.Micros[cursor] < bestMicros)
            {
                best = r;
                bestMicros = recording.Micros[cursor];
            }
        }

        return best;
    }

    /// <summary>Copies the frame, or only its data in raw mode, with the event time replaced by the send time.</summary>
    private static int BuildFrame(FrameRecording recording, int frame, Connection connection)
    {
        int frameOffset = recording.Offsets[frame];
        int start = connection.IsRaw ? recording.DataStarts[frame] : 0;
        int length = connection.IsRaw ? recording.DataLengths[frame] : recording.Lengths[frame];
        ReadOnlySpan<byte> source = recording.Blob.AsSpan(frameOffset + start, length);

        if (connection.Buffer.Length < length + MaxEventTimeDigits)
        {
            connection.Buffer = new byte[(length + MaxEventTimeDigits) * 2];
        }

        Span<byte> destination = connection.Buffer;
        int eventTime = recording.EventTimeStarts[frame];

        if (eventTime < 0)
        {
            source.CopyTo(destination);
            return length;
        }

        int digitsStart = eventTime - start;
        int digitsEnd = digitsStart + recording.EventTimeLengths[frame];

        source[..digitsStart].CopyTo(destination);
        Utf8Formatter.TryFormat(NowMicros(), destination[digitsStart..], out int written);

        ReadOnlySpan<byte> tail = source[digitsEnd..];
        tail.CopyTo(destination[(digitsStart + written)..]);

        return digitsStart + written + tail.Length;
    }

    /// <summary>Machine-wide monotonic clock in microseconds. Clients read the same clock.</summary>
    private static long NowMicros() => (long)(Stopwatch.GetTimestamp() * _microsPerTick);

    private static long ElapsedMicros(long startTicks) => (long)((Stopwatch.GetTimestamp() - startTicks) * _microsPerTick);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint periodMillis);

    [SuppressMessage("Design", "CA1001", Justification = "SemaphoreSlim without a wait handle holds no unmanaged resource.")]
    private sealed class Connection(int streamCount, bool isRaw)
    {
        public readonly bool[] Subscribed = new bool[streamCount];
        public readonly bool IsRaw = isRaw;
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public byte[] Buffer = new byte[64 * 1024];
        public int SubscribedCount;
        public long LastChangeTicks;
    }
}
