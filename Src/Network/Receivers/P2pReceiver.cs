using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace FileTransmitter;

internal sealed class P2pReceiver : IFileReceiver
{
    private const int ConnectTimeoutMs = 15000;

    private readonly IPEndPoint[] _endPoints;
    private readonly byte[] _code;
    private readonly string _saveDirectory;

    private TcpClient? _client;

    public P2pReceiver(IPEndPoint[] endPoints, byte[] code, string saveDirectory)
    {
        _endPoints = endPoints;
        _code = code;
        _saveDirectory = saveDirectory;
    }

    public async Task<bool> ReceiveAsync(CancellationToken token)
    {
        _client = await ConnectAsync(token);

        if (_client is null)
        {
            FTViewer.PrintMessage("Error: Could not connect to the sender.\n", ConsoleColor.Red);
            return false;
        }

        try
        {
            using NetworkStream stream = _client.GetStream();

            P2pSession? session = await P2pHandshake.PerformAsync(stream, isListener: false, _code, token);

            if (session is null)
            {
                FTViewer.PrintMessage("Verification failed.\n", ConsoleColor.Red);
                return false;
            }

            return await ReceiveFileAsync(stream, session, token);
        }
        catch (IOException)
        {
            FTViewer.PrintMessage("Error: Connection to the sender was lost.\n", ConsoleColor.Red);
            return false;
        }
        catch (SocketException)
        {
            FTViewer.PrintMessage("Error: Connection to the sender was lost.\n", ConsoleColor.Red);
            return false;
        }
    }

    private async Task<TcpClient?> ConnectAsync(CancellationToken token)
    {
        using var timeoutCts = new CancellationTokenSource(ConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

        var attempts = _endPoints
            .Select(endPoint => TryConnectAsync(endPoint, linkedCts.Token))
            .ToList();

        while (attempts.Count > 0)
        {
            Task<TcpClient?> finished = await Task.WhenAny(attempts);
            attempts.Remove(finished);

            TcpClient? client = await finished;

            if (client is not null)
            {
                linkedCts.Cancel();
                return client;
            }
        }

        return null;
    }

    private static async Task<TcpClient?> TryConnectAsync(IPEndPoint endPoint, CancellationToken token)
    {
        var client = new TcpClient();

        try
        {
            await client.ConnectAsync(endPoint.Address, endPoint.Port, token);
            return client;
        }
        catch
        {
            client.Dispose();
            return null;
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
        var progressBar = new ProgressBar();

        progressBar.Report(totalReceived, fileSize);

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

            progressBar.Report(totalReceived, fileSize);
        }

        progressBar.Report(fileSize, fileSize);

        var (doneType, donePayload) = await P2pFraming.ReadMessageAsync(stream, token);

        if (doneType != P2pMessageType.Done)
            return false;

        long confirmedSize = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(donePayload.AsSpan(0, 8));

        return confirmedSize == fileSize && totalReceived == fileSize;
    }
}