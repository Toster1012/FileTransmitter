using System.Security.Cryptography;

namespace FileTransmitter;

internal sealed class P2pSession
{
    public byte[] EncryptionKey { get; }

    public P2pSession(byte[] encryptionKey)
    {
        EncryptionKey = encryptionKey;
    }
}

internal static class P2pHandshake
{
    private static readonly byte[] EncryptionInfo = System.Text.Encoding.UTF8.GetBytes("FileTransmitter-P2P-encryption");

    public static async Task<P2pSession?> PerformAsync(Stream stream, bool isListener, byte[] code, CancellationToken token)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] ownPublicKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();

        await P2pFraming.WriteMessageAsync(stream, P2pMessageType.PublicKey, ownPublicKey, token);
        var (peerType, peerPublicKeyBytes) = await P2pFraming.ReadMessageAsync(stream, token);

        if (peerType != P2pMessageType.PublicKey)
            return null;

        using var peerEcdh = ECDiffieHellman.Create();
        peerEcdh.ImportSubjectPublicKeyInfo(peerPublicKeyBytes, out _);

        byte[] sharedSecret = ecdh.DeriveRawSecretAgreement(peerEcdh.PublicKey);

        byte[] listenerPublicKey = isListener ? ownPublicKey : peerPublicKeyBytes;
        byte[] connectorPublicKey = isListener ? peerPublicKeyBytes : ownPublicKey;

        byte[] expectedTag = ComputeAuthTag(code, listenerPublicKey, connectorPublicKey, sharedSecret);

        await P2pFraming.WriteMessageAsync(stream, P2pMessageType.AuthTag, expectedTag, token);
        var (tagType, peerTag) = await P2pFraming.ReadMessageAsync(stream, token);

        if (tagType != P2pMessageType.AuthTag || peerTag.Length != expectedTag.Length || !CryptographicOperations.FixedTimeEquals(peerTag, expectedTag))
            return null;

        byte[] encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, info: EncryptionInfo);

        return new P2pSession(encryptionKey);
    }

    private static byte[] ComputeAuthTag(byte[] code, byte[] listenerPublicKey, byte[] connectorPublicKey, byte[] sharedSecret)
    {
        byte[] data = new byte[listenerPublicKey.Length + connectorPublicKey.Length + sharedSecret.Length];
        int offset = 0;

        Array.Copy(listenerPublicKey, 0, data, offset, listenerPublicKey.Length);
        offset += listenerPublicKey.Length;

        Array.Copy(connectorPublicKey, 0, data, offset, connectorPublicKey.Length);
        offset += connectorPublicKey.Length;

        Array.Copy(sharedSecret, 0, data, offset, sharedSecret.Length);

        return HMACSHA256.HashData(code, data);
    }
}

internal sealed class P2pCipher : IDisposable
{
    private readonly ChaCha20Poly1305 _cipher;

    public P2pCipher(byte[] key)
    {
        _cipher = new ChaCha20Poly1305(key);
    }

    public byte[] Encrypt(long chunkIndex, byte[] plaintext)
    {
        byte[] nonce = BuildNonce(chunkIndex);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        _cipher.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] result = new byte[ciphertext.Length + tag.Length];
        Array.Copy(ciphertext, 0, result, 0, ciphertext.Length);
        Array.Copy(tag, 0, result, ciphertext.Length, tag.Length);

        return result;
    }

    public byte[] Decrypt(long chunkIndex, byte[] ciphertextWithTag)
    {
        if (ciphertextWithTag.Length < 16)
            throw new InvalidDataException("Chunk too short.");

        byte[] nonce = BuildNonce(chunkIndex);
        int cipherLength = ciphertextWithTag.Length - 16;

        byte[] ciphertext = new byte[cipherLength];
        byte[] tag = new byte[16];

        Array.Copy(ciphertextWithTag, 0, ciphertext, 0, cipherLength);
        Array.Copy(ciphertextWithTag, cipherLength, tag, 0, 16);

        byte[] plaintext = new byte[cipherLength];
        _cipher.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static byte[] BuildNonce(long chunkIndex)
    {
        byte[] nonce = new byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4, 8), chunkIndex);
        return nonce;
    }

    public void Dispose()
    {
        _cipher.Dispose();
    }
}