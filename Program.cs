using System.Runtime.InteropServices;

namespace FileTransmitter;

public sealed class Program
{
    private static readonly ConsoleCtrlDelegate _consoleCtrlDelegate = new(HandleWin32Close);

    private static string? _path;
    private static bool _isZip;
    private static bool _isFastedPack;
    private static bool isDownload = true;

    private delegate bool ConsoleCtrlDelegate(int sig);

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        FTViewer.PrintBanner();

        if (!ArgumentProcess(ref args))
            return;

        var (success, path, isZip) = await Utils.TryGetPath(args, _isFastedPack);

        if (!success)
            return;

        _path = path;
        _isZip = isZip;

        try
        {
            Console.CancelKeyPress += HandleConsoleCancel;
            AppDomain.CurrentDomain.ProcessExit += HandleProccesExit;
            SetConsoleCtrlHandler(_consoleCtrlDelegate, true);

            Start(_path);
        }
        finally
        {
            try
            {
                if (_isZip)
                    File.Delete(_path);
            }
            catch
            {
            }
        }
    }

    private static bool ArgumentProcess(ref string[] args)
    {
        if (args.Length == 0)
        {
            FTViewer.ShowHelpCommand();
            return false;
        }

        if (args[0].Equals("--help"))
        {
            FTViewer.ShowHelpsCommand();
            return false;
        }

        if (args[0].Equals("-w"))
        {
            isDownload = false;
            args = args.AsSpan(1).ToArray();
            return true;
        }

        if (args[0].Equals("-f"))
        {
            _isFastedPack = true;
            args = args.AsSpan(1).ToArray();
            return true;
        }

        if (args.Length == 0)
        {
            FTViewer.ShowHelpCommand();
            return false;
        }

        return true;
    }

    private static void Start(string path)
    {
        if (!File.Exists(path))
            return;

        var server = new FTServer();

        try
        {
            if (server.Start(path, isDownload))
            {
                Console.WriteLine();

                string? ip = Utils.GetIp();

                if (!string.IsNullOrEmpty(ip))
                {
                    FTViewer.PrintMessage("Scan QR code or open link to download:", ConsoleColor.Yellow);

                    string link = $"http://{ip}:{FTServer.HttpPort}/";

                    FTViewer.PrintMessage(QrViewer.GetCodeView(link), ConsoleColor.White);
                    FTViewer.PrintMessage($"     {link}", ConsoleColor.Cyan);
                }

                FTViewer.PrintMessage("\n ─────────────────────────────────────────────", ConsoleColor.DarkGray);
                FTViewer.PrintMessage(" Press ", ConsoleColor.DarkGray, true);
                FTViewer.PrintMessage("CTRL + Q", ConsoleColor.Yellow, true);
                FTViewer.PrintMessage(" to stop the server.", ConsoleColor.White);
                FTViewer.PrintMessage("", ConsoleColor.White);

                while (true)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                    bool hasCtrl = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                    if (hasCtrl && keyInfo.Key == ConsoleKey.Q)
                    {
                        server.Stop();
                        return;
                    }
                }
            }
        }
        catch
        {
            throw;
        }
    }

    private static void HandleProccesExit(object? sender, EventArgs e)
    {
        HandleConsoleCancel(sender, null);
    }

    private static void HandleConsoleCancel(object? sender, ConsoleCancelEventArgs? e)
    {
        try
        {
            if (string.IsNullOrEmpty(_path))
                return;

            if (!File.Exists(_path))
                return;

            if (_isZip)
                File.Delete(_path);
        }
        catch
        {
        }
    }

    private static bool HandleWin32Close(int sig)
    {
        if (sig == 2)
            HandleConsoleCancel(null, null);

        return false;
    }

    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);
}