using System.Diagnostics;

namespace FileTransmitter;

internal sealed class FirewallManager
{
    private const string RuleName = "FileTransmitter P2P";

    private bool _added;

    public async Task<bool> TryAddRuleAsync(int port, CancellationToken token)
    {
        int? exitCode = await RunNetshAsync($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}", token);

        _added = exitCode == 0;
        return _added;
    }

    public void RemoveRule()
    {
        if (!_added)
            return;

        RunNetshAsync($"advfirewall firewall delete rule name=\"{RuleName}\"", CancellationToken.None).GetAwaiter().GetResult();

        _added = false;
    }

    private static async Task<int?> RunNetshAsync(string arguments, CancellationToken token)
    {
        try
        {
            var startInfo = new ProcessStartInfo("netsh", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);

            if (process is null)
                return null;

            await process.WaitForExitAsync(token);

            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }
}
