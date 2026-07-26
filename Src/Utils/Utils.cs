using System.Net;
using System.Net.Sockets;

namespace FileTransmitter;

internal static class Utils
{
    public static string? GetIp()
    {
        var hostEntry = Dns.GetHostEntry(Dns.GetHostName());

        var ip = hostEntry.AddressList
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Where(ip => !IPAddress.IsLoopback(ip))
            .Where(ip => !ip.ToString().StartsWith("169.254.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(ip => !ip.ToString().StartsWith("192.168.", StringComparison.OrdinalIgnoreCase))
            .ThenBy(ip => !ip.ToString().StartsWith("10.", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return ip?.ToString();
    }

    public static async Task<(bool Success, string Path, bool IsZip)> TryGetPath(string[] args, bool isFastedPack)
    {
        if (args.Length == 0)
            return (false, string.Empty, false);

        string path = string.Join(' ', args);

        if (string.IsNullOrEmpty(path))
        {
            FTViewer.PrintMessage("Error: Path is empty.\n", ConsoleColor.Red);
            return (false, string.Empty, false);
        }

        if (!File.Exists(path))
        {
            if (!Directory.Exists(path))
            {
                FTViewer.PrintMessage($"Error: Target not found -> \"{path}\"\n", ConsoleColor.Red);
                return (false, string.Empty, false);
            }
            else
            {
                var (result, archivePath) = await ZipArchiver.TryPackDirectory(path, isFastedPack);
                path = archivePath;

                if (!result)
                {
                    FTViewer.PrintMessage("Error: Directory compression failed.\n", ConsoleColor.Red);
                    return (false, string.Empty, false);
                }

                return (true, path, true);
            }
        }

        return (true, path, false);
    }
}