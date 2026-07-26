using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace FileTransmitter;

internal sealed class P2pSender : IFileSender
{
    private const int ChunkSize = 65536;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private PortForwarder? _portForwarder;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task<bool> StartAsync(string path, bool isDownload, CancellationToken token)
    {
        if (!File.Exists(path))
            return false;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var stunClient = new StunClient();
        IPAddress? publicIp = await stunClient.GetPublicIpAsync(_cts.Token);

        if (publicIp is null)
        {
            FTViewer.PrintMessage("Error: Failed to determine public IP address.\n", ConsoleColor.Red);
            return false;
        }

        _portForwarder = new PortForwarder();
        bool mapped = await _portForwarder.TryMapPortAsync(Config.P2pPort, _cts.Token);

        if (!mapped)
        {
            FTViewer.PrintMessage("Error: Failed to open port automatically. Configure port forwarding manually.\n", ConsoleColor.Red);
            return false;
        }

        byte[] code = RandomNumberGenerator.GetBytes(Config.P2pCodeByteLength);
        string codeText = Base32.Encode(code);

        ConnectionString = $"{publicIp}:{Config.P2pPort}:{codeText}";

        _listener = new TcpListener(IPAddress.Any, Config.P2pPort);
        _listener.Start();

        _ = Task.Run(() => AcceptRoutine(path, code, _cts.Token), _cts.Token);

        return true;
    }

    public void Stop()
    {
        try
        {
            _listener?.Stop();
            _cts?.Cancel();
            _portForwarder?.RemoveMappingAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task AcceptRoutine(string path, byte[] code, CancellationToken token)
    {
        try
        {
            using TcpClient client = await _listener!.AcceptTcpClientAsync(token);
            using NetworkStream stream = client.GetStream();

            P2pSession? session = await P2pHandshake.PerformAsync(stream, isListener: true, code, token);

            if (session is null)
                return;

            await SendFileAsync(stream, session, path, token);
        }
        catch
        {
        }
    }

    private static async Task SendFileAsync(NetworkStream stream, P2pSession session, string path, CancellationToken token)
    {
        var fileInfo = new FileInfo(path);

        byte[] fileNameBytes = System.Text.Encoding.UTF8.GetBytes(fileInfo.Name);
        byte[] metadata = new byte[2 + fileNameBytes.Length + 8 + 4];

        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(metadata.AsSpan(0, 2), (ushort)fileNameBytes.Length);
        Array.Copy(fileNameBytes, 0, metadata, 2, fileNameBytes.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(metadata.AsSpan(2 + fileNameBytes.Length, 8), fileInfo.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(metadata.AsSpan(2 + fileNameBytes.Length + 8, 4), ChunkSize);

        await P2pFraming.WriteMessageAsync(stream, P2pMessageType.Metadata, metadata, token);

        var (resumeType, resumePayload) = await P2pFraming.ReadMessageAsync(stream, token);

        if (resumeType != P2pMessageType.ResumeRequest)
            return;

        long resumeOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(resumePayload.AsSpan(0, 8));
        byte[] resumeHash = resumePayload[8..];

        using var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        long acceptedOffset = 0;

        if (resumeOffset > 0 && resumeOffset <= fileInfo.Length)
        {
            byte[] localHash = await P2pFileHasher.ComputeRangeHashAsync(fileStream, 0, resumeOffset, token);

            if (CryptographicOperations.FixedTimeEquals(localHash, resumeHash))
                acceptedOffset = resumeOffset;
        }

        byte[] ackPayload = new byte[9];
        ackPayload[0] = acceptedOffset > 0 ? (byte)0x01 : (byte)0x00;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(ackPayload.AsSpan(1, 8), acceptedOffset);

        await P2pFraming.WriteMessageAsync(stream, P2pMessageType.ResumeAck, ackPayload, token);

        fileStream.Position = acceptedOffset;

        using var cipher = new P2pCipher(session.EncryptionKey);

        long chunkIndex = acceptedOffset / ChunkSize;
        byte[] buffer = new byte[ChunkSize];
        int bytesRead;

        while ((bytesRead = await fileStream.ReadAsync(buffer, token)) > 0)
        {
            byte[] plaintext = bytesRead == buffer.Length ? buffer : buffer[..bytesRead];
            byte[] encrypted = cipher.Encrypt(chunkIndex, plaintext);

            byte[] chunkPayload = new byte[8 + encrypted.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(chunkPayload.AsSpan(0, 8), chunkIndex);
            Array.Copy(encrypted, 0, chunkPayload, 8, encrypted.Length);

            await P2pFraming.WriteMessageAsync(stream, P2pMessageType.Chunk, chunkPayload, token);

            chunkIndex++;
        }

        byte[] donePayload = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(donePayload, fileInfo.Length);

        await P2pFraming.WriteMessageAsync(stream, P2pMessageType.Done, donePayload, token);
    }
}