using System.Net.Sockets;
using System.Security.Cryptography;

namespace FileTransmitter;

internal static class P2pFileTransfer
{
    private const int ChunkSize = 65536;

    public static async Task<bool> SendFileAsync(NetworkStream stream, P2pSession session, string path, CancellationToken token)
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
            return false;

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

        return true;
    }

    public static async Task<bool> ReceiveFileAsync(NetworkStream stream, P2pSession session, string saveDirectory, CancellationToken token)
    {
        var (metadataType, metadataPayload) = await P2pFraming.ReadMessageAsync(stream, token);

        if (metadataType != P2pMessageType.Metadata)
            return false;

        ushort nameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(metadataPayload.AsSpan(0, 2));
        string fileName = System.Text.Encoding.UTF8.GetString(metadataPayload, 2, nameLength);
        long fileSize = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(metadataPayload.AsSpan(2 + nameLength, 8));

        Directory.CreateDirectory(saveDirectory);
        string targetPath = Path.Combine(saveDirectory, fileName);

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
