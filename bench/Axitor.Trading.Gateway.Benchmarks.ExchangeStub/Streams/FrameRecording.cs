using System.Buffers.Binary;
using System.Text;

namespace Axitor.Trading.Gateway.Benchmarks.ExchangeStub.Streams;

/// <summary>One recorded frame file held in memory, with the stream, data bounds and event time field of each frame indexed.</summary>
internal sealed class FrameRecording
{
    private const int MaxStackName = 128;
    private const int RecordHeaderBytes = sizeof(long) + sizeof(byte) + sizeof(int);

    private static ReadOnlySpan<byte> Magic => "AXREC001"u8;
    private static ReadOnlySpan<byte> StreamPrefix => "{\"stream\":\""u8;
    private static ReadOnlySpan<byte> DataSeparator => "\",\"data\":"u8;
    private static ReadOnlySpan<byte> EventTimeKey => "\"E\":"u8;

    private FrameRecording(string name, byte[] blob, int count)
    {
        Name = name;
        Blob = blob;
        Count = count;
        Micros = new long[count];
        Offsets = new int[count];
        Lengths = new int[count];
        StreamIds = new int[count];
        DataStarts = new int[count];
        DataLengths = new int[count];
        EventTimeStarts = new int[count];
        EventTimeLengths = new int[count];
    }

    public string Name { get; }

    /// <summary>The whole file; frames are read from it in place.</summary>
    public byte[] Blob { get; }

    public int Count { get; }

    /// <summary>When each frame arrived, microseconds since the recording started.</summary>
    public long[] Micros { get; }

    public int[] Offsets { get; }

    public int[] Lengths { get; }

    /// <summary>Stream of each frame, or -1 for a frame that is not stream data.</summary>
    public int[] StreamIds { get; }

    /// <summary>Where the data object starts, from the frame start.</summary>
    public int[] DataStarts { get; }

    public int[] DataLengths { get; }

    /// <summary>Where the digits of the event time start, from the frame start, or -1 when the frame has none.</summary>
    public int[] EventTimeStarts { get; }

    public int[] EventTimeLengths { get; }

    /// <summary>Reads a frame file and gives every new stream name the next id.</summary>
    public static FrameRecording Load(string path, Dictionary<string, int> streamIds)
    {
        byte[] blob = File.ReadAllBytes(path);

        if (!blob.AsSpan().StartsWith(Magic))
        {
            throw new InvalidDataException($"{path} is not a frame recording.");
        }

        FrameRecording recording = new(Path.GetFileNameWithoutExtension(path), blob, CountFrames(blob));
        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> lookup = streamIds.GetAlternateLookup<ReadOnlySpan<char>>();
        int position = Magic.Length;

        for (int i = 0; i < recording.Count; i++)
        {
            ReadOnlySpan<byte> header = blob.AsSpan(position, RecordHeaderBytes);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header[(sizeof(long) + sizeof(byte))..]);
            bool isBinary = header[sizeof(long)] == 1;

            recording.Micros[i] = BinaryPrimitives.ReadInt64LittleEndian(header);
            recording.Offsets[i] = position + RecordHeaderBytes;
            recording.Lengths[i] = length;

            IndexFrame(recording, i, isBinary, streamIds, lookup);
            position += RecordHeaderBytes + length;
        }

        return recording;
    }

    private static int CountFrames(byte[] blob)
    {
        int count = 0;
        int position = Magic.Length;

        while (position + RecordHeaderBytes <= blob.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(position + sizeof(long) + sizeof(byte), sizeof(int)));

            // A frame cut off at the end of the file is left out.
            if (blob.Length < position + RecordHeaderBytes + length)
            {
                break;
            }

            position += RecordHeaderBytes + length;
            count++;
        }

        return count;
    }

    private static void IndexFrame(
        FrameRecording recording,
        int index,
        bool isBinary,
        Dictionary<string, int> streamIds,
        Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> lookup)
    {
        recording.StreamIds[index] = -1;
        recording.EventTimeStarts[index] = -1;

        ReadOnlySpan<byte> frame = recording.Blob.AsSpan(recording.Offsets[index], recording.Lengths[index]);

        if (isBinary || !frame.StartsWith(StreamPrefix))
        {
            return;
        }

        int nameLength = frame[StreamPrefix.Length..].IndexOf((byte)'"');
        int nameEnd = StreamPrefix.Length + nameLength;

        if (nameLength <= 0 || !frame[nameEnd..].StartsWith(DataSeparator) || frame[^1] != (byte)'}')
        {
            return;
        }

        ReadOnlySpan<byte> name = frame.Slice(StreamPrefix.Length, nameLength);
        int dataStart = nameEnd + DataSeparator.Length;
        int dataLength = frame.Length - 1 - dataStart;

        recording.StreamIds[index] = FindOrAddStream(name, streamIds, lookup);
        recording.DataStarts[index] = dataStart;
        recording.DataLengths[index] = dataLength;

        ReadOnlySpan<byte> data = frame.Slice(dataStart, dataLength);
        int key = data.IndexOf(EventTimeKey);

        if (key < 0)
        {
            return;
        }

        int digitsStart = key + EventTimeKey.Length;
        int digits = data[digitsStart..].IndexOfAnyExceptInRange((byte)'0', (byte)'9');

        if (0 < digits)
        {
            recording.EventTimeStarts[index] = dataStart + digitsStart;
            recording.EventTimeLengths[index] = digits;
        }
    }

    private static int FindOrAddStream(ReadOnlySpan<byte> name, Dictionary<string, int> streamIds, Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> lookup)
    {
        if (name.Length <= MaxStackName)
        {
            Span<char> chars = stackalloc char[MaxStackName];
            int written = Encoding.ASCII.GetChars(name, chars);

            if (lookup.TryGetValue(chars[..written], out int found))
            {
                return found;
            }
        }

        string text = Encoding.ASCII.GetString(name);

        if (!streamIds.TryGetValue(text, out int id))
        {
            id = streamIds.Count;
            streamIds.Add(text, id);
        }

        return id;
    }
}
