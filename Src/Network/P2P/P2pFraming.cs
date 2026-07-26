using System.Buffers.Binary;

namespace FileTransmitter;

internal static class P2pFraming
{
    private const int MaxPayloadSize = 16 * 1024 * 1024;

    public static async Task WriteMessageAsync(Stream stream, P2pMessageType type, byte[] payload, CancellationToken token)
    {
        byte[] header = new byte[5];
        header[0] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1, 4), (uint)payload.Length);

        await stream.WriteAsync(header, token);

        if (payload.Length > 0)
            await stream.WriteAsync(payload, token);

        await stream.FlushAsync(token);
    }

    public static async Task<(P2pMessageType Type, byte[] Payload)> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[5];
        await ReadExactAsync(stream, header, token);

        var type = (P2pMessageType)header[0];
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1, 4));

        if (length > MaxPayloadSize)
            throw new InvalidDataException($"Payload too large: {length} bytes.");

        byte[] payload = length == 0 ? [] : new byte[length];

        if (length > 0)
            await ReadExactAsync(stream, payload, token);

        return (type, payload);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token);

            if (read == 0)
                throw new EndOfStreamException("Connection closed unexpectedly.");

            offset += read;
        }
    }
}