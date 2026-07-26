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
            if (_parser.Listen)
                await StartReceiveListen(_parser.Path);
            else
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

        if (_parser.TransferMode == TransferMode.P2pSend && _parser.Connect)
        {
            await StartSendConnect(path, _parser.ConnectEndPoints!, _parser.ConnectCode!);
            return;
        }

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

    private static async Task StartSendConnect(string path, IPEndPoint[] endPoints, byte[] code)
    {
        var sender = new P2pSendConnector(endPoints, code);

        void CancelHandler(object? cancelSender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            sender.Stop();
        }

        Console.CancelKeyPress += CancelHandler;

        try
        {
            FTViewer.PrintMessage("Connecting...", ConsoleColor.Yellow);

            bool success = await sender.SendAsync(path, CancellationToken.None);

            Console.WriteLine();

            FTViewer.PrintMessage(success ? "Transfer complete." : "Transfer failed.", success ? ConsoleColor.Green : ConsoleColor.Red);
        }
        finally
        {
            Console.CancelKeyPress -= CancelHandler;
            sender.Stop();
        }
    }

    private static async Task StartReceiveListen(string savePath)
    {
        var receiver = new P2pReceiveListener(savePath);

        try
        {
            if (await receiver.StartAsync(CancellationToken.None))
            {
                Console.WriteLine();

                FTViewer.PrintMessage("Give this to the other side to send the file:", ConsoleColor.Yellow);
                FTViewer.PrintMessage($"     ft -n -c {receiver.ConnectionString} -p <file>", ConsoleColor.Cyan);

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
                        receiver.Stop();
                        return;
                    }
                }
            }
        }
        finally
        {
            receiver.Stop();
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