using System.Text;

namespace FileTransmitter;

internal static class FTViewer
{
    public static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(@"
    ███████╗██╗████████╗███████╗
    ██╔════╝██║╚══██╔══╝██╔════╝
    █████╗  ██║   ██║   ███████╗
    ██╔══╝  ██║   ██║   ╚════██║
    ██║     ██║   ██║   ███████║
    ╚═╝     ╚═╝   ╚═╝   ╚══════╝");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"    File Transmitter v{Config.ProjectVersion}");
        Console.ResetColor();
        Console.WriteLine("    ─────────────────────────────────────────────\n");
    }

    public static async Task ShowAnimation(CancellationToken cancellationToken)
    {
        Console.CursorVisible = false;

        var (left, top) = Console.GetCursorPosition();
        char[] spinner = ['|', '/', '-', '\\'];
        int index = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.SetCursorPosition(left, top);
                Console.Write($"    {spinner[index++ % spinner.Length]}");

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Console.CursorVisible = true;

            Console.SetCursorPosition(left, top);
            Console.Write(' ');
            Console.SetCursorPosition(0, top + 1);
        }
    }

    public static void ShowHelpsCommand()
    {
        var builder = new StringBuilder();

        builder.AppendLine("    ft (argument not obligatory) file or folder path");
        builder.AppendLine("    -------------------------------------------------------------");
        builder.AppendLine($"    --help               | Print all command with description");
        builder.AppendLine($"    -w                   | Send file for writing other users");
        builder.AppendLine($"    -f                   | Fast pack archive");
        builder.AppendLine($"    -p                   | Specifies the path to the file or folder, must be last.");
        builder.AppendLine($"    -n                   | Send file through a secure p2p connection. Must be used with -p.");
        builder.AppendLine($"    -o                   | Receive a file through a secure p2p connection, requires ip:port:code. Must be used with -p (destination folder), the -o value must come before -p.");
        builder.AppendLine();
        builder.Append(BuildExamples());

        Console.WriteLine(builder.ToString());
    }

    private static string BuildExamples()
    {
        var builder = new StringBuilder();

        builder.AppendLine("    Examples:");
        builder.AppendLine("    -------------------------------------------------------------");
        builder.AppendLine("    ft -p C:\\file.zip                                   Share a file over LAN");
        builder.AppendLine("    ft -w -p C:\\file.zip                                Share a file over LAN, allow writing");
        builder.AppendLine("    ft -f -p C:\\folder                                  Pack a folder fast and share it over LAN");
        builder.AppendLine("    ft -n -p C:\\file.zip                                Send a file through a secure p2p connection");
        builder.AppendLine("    ft -o 203.0.113.5:8081:ABCD2XYZ -p C:\\downloads     Receive a file through p2p");

        return builder.ToString();
    }

    public static void PrintMessage(string message, ConsoleColor consoleColor, bool writeInCurrentLine = false)
    {
        Console.ForegroundColor = consoleColor;

        if (!writeInCurrentLine)
            Console.WriteLine(message);
        else
            Console.Write(message);

        Console.ResetColor();
    }

    public static void ShowHelpCommand()
    {
        FTViewer.PrintMessage($"    Usage: ft --help", ConsoleColor.White);
    }
}