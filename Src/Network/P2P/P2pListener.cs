using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace FileTransmitter;

internal sealed class P2pListener
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private PortForwarder? _portForwarder;
    private FirewallManager? _firewallManager;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task<bool> StartAsync(Func<NetworkStream, P2pSession, CancellationToken, Task<bool>> handler, CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        string? localIp = Utils.GetIp();

        var stunClient = new StunClient();
        IPAddress? publicIp = await stunClient.GetPublicIpAsync(_cts.Token);

        if (publicIp is null)
            FTViewer.PrintMessage("Warning: Failed to determine public IP address. Only local network transfer will be available.\n", ConsoleColor.Yellow);

        _portForwarder = new PortForwarder();
        bool mapped = await _portForwarder.TryMapPortAsync(Config.P2pPort, _cts.Token);

        if (!mapped)
            FTViewer.PrintMessage("Warning: Failed to open port automatically. Configure port forwarding manually for transfer outside your local network.\n", ConsoleColor.Yellow);

        _firewallManager = new FirewallManager();
        bool firewallOk = await _firewallManager.TryAddRuleAsync(Config.P2pPort, _cts.Token);

        if (!firewallOk)
            FTViewer.PrintMessage("Warning: Failed to add a Windows Firewall rule automatically. Allow the app through the firewall manually if the other side cannot connect.\n", ConsoleColor.Yellow);

        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(localIp))
            candidates.Add(localIp);

        if (publicIp is not null && publicIp.ToString() != localIp)
            candidates.Add(publicIp.ToString());

        if (candidates.Count == 0)
        {
            FTViewer.PrintMessage("Error: Failed to determine any reachable IP address.\n", ConsoleColor.Red);
            return false;
        }

        byte[] code = RandomNumberGenerator.GetBytes(Config.P2pCodeByteLength);
        string codeText = Base32.Encode(code);

        ConnectionString = $"{string.Join(',', candidates)}:{Config.P2pPort}:{codeText}";

        _listener = new TcpListener(IPAddress.Any, Config.P2pPort);
        _listener.Start();

        _ = Task.Run(() => AcceptRoutine(handler, code, _cts.Token), _cts.Token);

        return true;
    }

    public void Stop()
    {
        try
        {
            _listener?.Stop();
            _cts?.Cancel();
            _portForwarder?.RemoveMappingAsync().GetAwaiter().GetResult();
            _firewallManager?.RemoveRule();
        }
        catch
        {
        }
    }

    private async Task AcceptRoutine(Func<NetworkStream, P2pSession, CancellationToken, Task<bool>> handler, byte[] code, CancellationToken token)
    {
        while (true)
        {
            TcpClient client;

            try
            {
                client = await _listener!.AcceptTcpClientAsync(token);
            }
            catch
            {
                return;
            }

            try
            {
                using (client)
                {
                    using NetworkStream stream = client.GetStream();

                    P2pSession? session = await P2pHandshake.PerformAsync(stream, isListener: true, code, token);

                    if (session is null)
                        continue;

                    if (await handler(stream, session, token))
                        return;
                }
            }
            catch
            {
            }
        }
    }
}
