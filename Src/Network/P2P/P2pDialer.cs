using System.Net;
using System.Net.Sockets;

namespace FileTransmitter;

internal sealed class P2pDialer
{
    private const int ConnectTimeoutMs = 15000;

    private readonly IPEndPoint[] _endPoints;

    public IReadOnlyList<(IPEndPoint EndPoint, string Reason)> Failures { get; private set; } = [];

    public P2pDialer(IPEndPoint[] endPoints)
    {
        _endPoints = endPoints;
    }

    public async Task<TcpClient?> ConnectAsync(CancellationToken token)
    {
        using var timeoutCts = new CancellationTokenSource(ConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

        var failures = new List<(IPEndPoint, string)>();
        var attempts = _endPoints
            .Select(endPoint => TryConnectAsync(endPoint, linkedCts.Token))
            .ToList();

        while (attempts.Count > 0)
        {
            Task<(TcpClient? Client, IPEndPoint EndPoint, string? Reason)> finished = await Task.WhenAny(attempts);
            attempts.Remove(finished);

            var (client, endPoint, reason) = await finished;

            if (client is not null)
            {
                linkedCts.Cancel();
                Failures = failures;
                return client;
            }

            failures.Add((endPoint, reason ?? "unknown error"));
        }

        Failures = failures;
        return null;
    }

    private static async Task<(TcpClient? Client, IPEndPoint EndPoint, string? Reason)> TryConnectAsync(IPEndPoint endPoint, CancellationToken token)
    {
        var client = new TcpClient();

        try
        {
            await client.ConnectAsync(endPoint.Address, endPoint.Port, token);
            return (client, endPoint, null);
        }
        catch (SocketException ex)
        {
            client.Dispose();
            return (null, endPoint, DescribeSocketError(ex.SocketErrorCode));
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            return (null, endPoint, "timed out");
        }
        catch (Exception ex)
        {
            client.Dispose();
            return (null, endPoint, ex.Message);
        }
    }

    private static string DescribeSocketError(SocketError error)
    {
        return error switch
        {
            SocketError.ConnectionRefused => "connection refused",
            SocketError.TimedOut => "timed out",
            SocketError.HostUnreachable => "host unreachable",
            SocketError.NetworkUnreachable => "network unreachable",
            SocketError.HostNotFound => "host not found",
            SocketError.AddressNotAvailable => "address not available",
            _ => error.ToString(),
        };
    }
}
