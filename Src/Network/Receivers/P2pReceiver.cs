using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace FileTransmitter;

internal sealed class P2pReceiver : IFileReceiver
{
    private readonly IPEndPoint _endPoint;
    private readonly byte[] _code;
    private readonly string _saveDirectory;

    private TcpClient? _client;

    public P2pReceiver(IPEndPoint endPoint, byte[] code, string saveDirectory)
    {
        _endPoint = endPoint;
        _code = code;
        _saveDirectory = saveDirectory;
    }

    public async Task<bool> ReceiveAsync(CancellationToken token)
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_endPoint.Address, _endPoint.Port, token);

            using NetworkStream stream = _client.GetStream();

            P2pSession? session = await P2pHandshake.PerformAsync(stream, isListener: false, _code, token);

            if (session is null)
            {
                FTViewer.PrintMessage("Verification failed.\n", ConsoleColor.Red);
                return false;
            }

            return await ReceiveFileAsync(stream, session, token);
        }
        catch (SocketException)
        {
            FTViewer.PrintMessage("Error: Could not connect to the sender.\n", ConsoleColor.Red);
            return false;
        }
    }

    public void Stop()
    {
        try
        {
            _client?.Close();
        }
        catch
        {
        }
    }

    private async Task<bool> ReceiveFileAsync(NetworkStream stream, P2pSession session, CancellationToken token)
    {
        var (metadataType, metadataPayload) = await P2pFraming.ReadMessageAsync(stream, token);

        if (metadataType != P2pMessageType.Metadata)
            return false;

        ushort nameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(metadataPayload.AsSpan(0, 2));
        string fileName = System.Text.Encoding.UTF8.GetString(metadataPayload, 2, nameLength);
        long fileSize = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(metadataPayload.AsSpan(2 + nameLength, 8));

        Directory.CreateDirectory(_saveDirectory);
        string targetPath = Path.Combine(_saveDirectory, fileName);

        long existingLength = File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0;
        existingLength = Math.Min(existingLength, fileSize);

        byte[] existingHash;

        if (existingLength > 0)
        {
            using var existingStream = File.Open(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            existingHash = await P2pFileHasher.ComputeRangeHashAsync(existingStream, 0, existingLength, token);
        }
        else
        {
            existingHash = SHA256.HashData([]);
        }

        byte[] resumePayload = new byte[8 + existingHash.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(resumePayload.AsSpan(0, 8), existingLength);
        Array.Copy(existingHash, 0, resumePayload, 8, existingHash.Length);

        await P2pFraming.WriteMessageAsync(stream, P2pMessageType.ResumeRequest, resumePayload, token);

        var (ackType, ackPayload) = await P2pFraming.ReadMessageAsync(stream, token);

        if (ackType != P2pMessageType.ResumeAck)
            return false;

        long acceptedOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(ackPayload.AsSpan(1, 8));

        using var fileStream = new FileStream(targetPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        fileStream.SetLength(acceptedOffset);
        fileStream.Position = acceptedOffset;

        using var cipher = new P2pCipher(session.EncryptionKey);

        long totalReceived = acceptedOffset;

        while (totalReceived < fileSize)
        {
            var (chunkType, chunkPayload) = await P2pFraming.ReadMessageAsync(stream, token);

            if (chunkType != P2pMessageType.Chunk)
                return false;

            long chunkIndex = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(chunkPayload.AsSpan(0, 8));
            byte[] encrypted = chunkPayload[8..];

            byte[] plaintext = cipher.Decrypt(chunkIndex, encrypted);

            await fileStream.WriteAsync(plaintext, token);

            totalReceived += plaintext.Length;

            PrintProgress(totalReceived, fileSize);
        }

        var (doneType, donePayload) = await P2pFraming.ReadMessageAsync(stream, token);

        if (doneType != P2pMessageType.Done)
            return false;

        long confirmedSize = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(donePayload.AsSpan(0, 8));

        return confirmedSize == fileSize && totalReceived == fileSize;
    }

    private static void PrintProgress(long received, long total)
    {
        int percent = total == 0 ? 100 : (int)(received * 100 / total);
        Console.Write($"\r    {percent}% ({received}/{total} bytes)");
    }
}