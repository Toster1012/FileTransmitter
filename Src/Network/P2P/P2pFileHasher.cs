using System.Security.Cryptography;

namespace FileTransmitter;

internal static class P2pFileHasher
{
    public static async Task<byte[]> ComputeRangeHashAsync(FileStream fileStream, long start, long length, CancellationToken token)
    {
        fileStream.Position = start;

        using var sha256 = SHA256.Create();
        byte[] buffer = new byte[81920];
        long remaining = length;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), token);

            if (read == 0)
                break;

            sha256.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        sha256.TransformFinalBlock([], 0, 0);

        return sha256.Hash!;
    }
}