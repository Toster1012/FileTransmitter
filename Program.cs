using System.Net;
using System.Runtime.InteropServices;

namespace FileTransmitter;

public sealed class Program
{
    private static readonly ConsoleCtrlDelegate _consoleCtrlDelegate = new(HandleWin32Close);
    private static readonly ArgumentParser _parser = new();

    private static string? _path;
    private static bool _isZip;

    private delegate bool ConsoleCtrlDelegate(int sig);

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        FTViewer.PrintBanner();

        if (!ArgumentProcess(args))
            return;

        if (string.IsNullOrEmpty(_parser.Path))
            return;

        if (_parser.TransferMode == TransferMode.P2pReceive)
        {
            await StartReceive(_parser.Path, _parser.EndPoints!, _parser.Code!);
            return;
        }

        var (success, path, isZip) = await Utils.TryPreparePathAsync(_parser.Path, _parser.FastPack);

        if (!success)
            return;

        _path = path;
        _isZip = isZip;

        try
        {
            Console.CancelKeyPress += HandleConsoleCancel;
            AppDomain.CurrentDomain.ProcessExit += HandleProccesExit;
            SetConsoleCtrlHandler(_consoleCtrlDelegate, true);

            await Start(_path);
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

    private static bool ArgumentProcess(string[] args)
    {
        bool success = _parser.TryParse(args);

        if (success && _parser.Help)
        {
            FTViewer.ShowHelpsCommand();
            return false;
        }

        return success;
    }

    private static async Task Start(string path)
    {
        if (!File.Exists(path))
            return;

        IFileSender sender = CreateSender();

        try
        {
            if (await sender.StartAsync(path, !_parser.Write, CancellationToken.None))
            {
                Console.WriteLine();

                if (sender is P2pSender p2pSender)
                {
                    FTViewer.PrintMessage("Give this to the other side to receive the file:", ConsoleColor.Yellow);
                    FTViewer.PrintMessage($"     ft -o {p2pSender.ConnectionString} -p <folder>", ConsoleColor.Cyan);
                }
                else
                {
                    string? ip = Utils.GetIp();

                    if (!string.IsNullOrEmpty(ip))
                    {
                        FTViewer.PrintMessage("Scan QR code or open link to download:", ConsoleColor.Yellow);

                        string link = $"http://{ip}:{HttpLanSender.HttpPort}/";

                        FTViewer.PrintMessage(QrViewer.GetCodeView(link), ConsoleColor.White);
                        FTViewer.PrintMessage($"     {link}", ConsoleColor.Cyan);
                    }
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
                        sender.Stop();
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

    private static async Task StartReceive(string savePath, IPEndPoint[] endPoints, byte[] code)
    {
        var receiver = new P2pReceiver(endPoints, code, savePath);

        void CancelHandler(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            receiver.Stop();
        }

        Console.CancelKeyPress += CancelHandler;

        try
        {
            FTViewer.PrintMessage("Connecting...", ConsoleColor.Yellow);

            bool success = await receiver.ReceiveAsync(CancellationToken.None);

            Console.WriteLine();

            FTViewer.PrintMessage(success ? "Transfer complete." : "Transfer failed.", success ? ConsoleColor.Green : ConsoleColor.Red);
        }
        finally
        {
            Console.CancelKeyPress -= CancelHandler;
            receiver.Stop();
        }
    }

    private static IFileSender CreateSender()
    {
        return _parser.TransferMode switch
        {
            TransferMode.P2pSend => new P2pSender(),
            _ => new HttpLanSender(),
        };
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