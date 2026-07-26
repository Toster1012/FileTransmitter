using System.Net;
using System.Net.Sockets;

namespace FileTransmitter;

internal sealed class StunClient
{
    private const int ReceiveTimeoutMs = 3000;
    private const ushort BindingSuccessResponse = 0x0101;
    private const ushort XorMappedAddress = 0x0020;
    private const ushort MappedAddress = 0x0001;

    private static readonly byte[] MagicCookie = [0x21, 0x12, 0xA4, 0x42];

    public async Task<IPAddress?> GetPublicIpAsync(CancellationToken token)
    {
        foreach (string serverEntry in Config.StunServer)
        {
            token.ThrowIfCancellationRequested();

            IPAddress? result = await TryQueryServerAsync(serverEntry, token);

            if (result is not null)
                return result;
        }

        return null;
    }

    private static async Task<IPAddress?> TryQueryServerAsync(string serverEntry, CancellationToken token)
    {
        try
        {
            var (host, port) = ParseServerEntry(serverEntry);

            if (host is null)
                return null;

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, token);

            if (addresses.Length == 0)
                return null;

            using var client = new UdpClient();
            client.Client.SendTimeout = ReceiveTimeoutMs;

            client.Connect(addresses[0], port);

            byte[] transactionId = new byte[12];
            Random.Shared.NextBytes(transactionId);

            byte[] request = BuildBindingRequest(transactionId);

            await client.SendAsync(request, token);

            using var timeoutCts = new CancellationTokenSource(ReceiveTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

            UdpReceiveResult receiveResult = await client.ReceiveAsync(linkedCts.Token);

            return ParseResponse(receiveResult.Buffer, transactionId);
        }
        catch (SocketException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }

    private static (string? Host, int Port) ParseServerEntry(string serverEntry)
    {
        string[] parts = serverEntry.Split(':');

        if (parts.Length != 2)
            return (null, 0);

        if (!int.TryParse(parts[1], out int port))
            return (null, 0);

        return (parts[0], port);
    }

    private static byte[] BuildBindingRequest(byte[] transactionId)
    {
        byte[] request = new byte[20];

        request[0] = 0x00;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = 0x00;

        Array.Copy(MagicCookie, 0, request, 4, 4);
        Array.Copy(transactionId, 0, request, 8, 12);

        return request;
    }

    private static IPAddress? ParseResponse(byte[] response, byte[] expectedTransactionId)
    {
        if (response.Length < 20)
            return null;

        int messageType = (response[0] << 8) | response[1];

        if (messageType != BindingSuccessResponse)
            return null;

        for (int i = 0; i < 4; i++)
        {
            if (response[4 + i] != MagicCookie[i])
                return null;
        }

        for (int i = 0; i < 12; i++)
        {
            if (response[8 + i] != expectedTransactionId[i])
                return null;
        }

        int index = 20;

        while (index + 4 <= response.Length)
        {
            int type = (response[index] << 8) | response[index + 1];
            int length = (response[index + 2] << 8) | response[index + 3];

            if (index + 4 + length > response.Length)
                break;

            if ((type == XorMappedAddress || type == MappedAddress) && length >= 8)
            {
                int family = response[index + 5];

                if (family == 0x01)
                {
                    byte[] ipBytes = new byte[4];

                    if (type == XorMappedAddress)
                    {
                        for (int i = 0; i < 4; i++)
                            ipBytes[i] = (byte)(response[index + 8 + i] ^ MagicCookie[i]);
                    }
                    else
                    {
                        Array.Copy(response, index + 8, ipBytes, 0, 4);
                    }

                    return new IPAddress(ipBytes);
                }
            }

            int paddedLength = (length + 3) & ~3;
            index += 4 + paddedLength;
        }

        return null;
    }
}