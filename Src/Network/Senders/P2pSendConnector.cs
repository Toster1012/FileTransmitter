using System.Net;
using System.Net.Sockets;

namespace FileTransmitter;

internal sealed class P2pSendConnector
{
    private readonly IPEndPoint[] _endPoints;
    private readonly byte[] _code;

    private TcpClient? _client;

    public P2pSendConnector(IPEndPoint[] endPoints, byte[] code)
    {
        _endPoints = endPoints;
        _code = code;
    }

    public async Task<bool> SendAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path))
            return false;

        var dialer = new P2pDialer(_endPoints);
        _client = await dialer.ConnectAsync(token);

        if (_client is null)
        {
            FTViewer.PrintMessage("Error: Could not connect to the receiver.", ConsoleColor.Red);
            FTViewer.PrintConnectFailures(dialer.Failures);
            Console.WriteLine();
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

            return await P2pFileTransfer.SendFileAsync(stream, session, path, token);
        }
        catch (IOException)
        {
            FTViewer.PrintMessage("Error: Connection to the receiver was lost.\n", ConsoleColor.Red);
            return false;
        }
        catch (SocketException)
        {
            FTViewer.PrintMessage("Error: Connection to the receiver was lost.\n", ConsoleColor.Red);
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
}
