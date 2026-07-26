using System.Net;

namespace FileTransmitter;

internal sealed class ArgumentParser
{
    private List<string>? _arguments;
    private string? _path;
    private byte[]? _code;
    private IPEndPoint[]? _endPoints;

    public bool TryParse(string[] args)
    {
        return ParseArgument(args, out _arguments, out _path);
    }

    public string? Path => _path;
    public IPEndPoint[]? EndPoints => _endPoints;
    public byte[]? Code => _code;
    public bool Help => IsSet(Config.HelpArgument);
    public bool Write => IsSet(Config.WriteArgument);
    public bool FastPack => IsSet(Config.FastPackArgument);
    public TransferMode TransferMode => GetTransferModeInternal();

    private bool ParseArgument(string[] args, out List<string>? parsedArgs, out string filePath)
    {
        filePath = string.Empty;
        parsedArgs = null;

        if (args.Length == 0)
        {
            FTViewer.ShowHelpCommand();
            return false;
        }

        parsedArgs = [.. args.Where(a => a.StartsWith('-'))];

        if (Help)
            return true;

        if (!ParseP2P(args))
            return false;

        if (!ParsePath(parsedArgs))
            return false;

        filePath = string.Join(' ', args
            .SkipWhile(a => a != Config.PathArgument)
            .Skip(1)
            .ToArray());

        return true;
    }

    private bool IsSet(string argument) => _arguments is not null && _arguments.Contains(argument);

    private TransferMode GetTransferModeInternal()
    {
        if (IsSet(Config.DownloadP2P) && IsSet(Config.SendP2P))
            return TransferMode.None;

        if (IsSet(Config.DownloadP2P))
            return TransferMode.P2pReceive;

        if (IsSet(Config.SendP2P))
            return TransferMode.P2pSend;

        return TransferMode.LanServe;
    }

    private bool ParseP2P(string[] args)
    {
        if (TransferMode == TransferMode.P2pReceive)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(Config.DownloadP2P))
                {
                    if (i == args.Length - 1)
                    {
                        FTViewer.PrintMessage("Failed parse end point.", ConsoleColor.Red);
                        return false;
                    }

                    if (args[i + 1].StartsWith('-'))
                    {
                        FTViewer.PrintMessage("Endpoint is not define.", ConsoleColor.Red);
                        return false;
                    }

                    var splitEndPoint = args[i + 1].Split(':');

                    if (splitEndPoint.Length != 3)
                    {
                        FTViewer.PrintMessage($"Invalid end point format: {args[i + 1]}", ConsoleColor.Red);
                        return false;
                    }

                    string[] addressParts = splitEndPoint[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var addresses = new List<IPAddress>();

                    foreach (string addressPart in addressParts)
                    {
                        if (!IPAddress.TryParse(addressPart, out var parsedAddress))
                        {
                            FTViewer.PrintMessage($"Failed parse ip: {addressPart}", ConsoleColor.Red);
                            return false;
                        }

                        addresses.Add(parsedAddress);
                    }

                    if (addresses.Count == 0 || !int.TryParse(splitEndPoint[1], out var port))
                    {
                        FTViewer.PrintMessage($"Failed parse ip: {splitEndPoint[0]} or port: {splitEndPoint[1]} point.", ConsoleColor.Red);
                        return false;
                    }

                    if (port < 0 || port > 65535)
                    {
                        FTViewer.PrintMessage($"Port out of range: {port}", ConsoleColor.Red);
                        return false;
                    }

                    byte[] code;

                    try
                    {
                        code = Base32.Decode(splitEndPoint[2]);
                    }
                    catch (FormatException)
                    {
                        FTViewer.PrintMessage($"Invalid code: {splitEndPoint[2]}", ConsoleColor.Red);
                        return false;
                    }

                    if (code.Length != Config.P2pCodeByteLength)
                    {
                        FTViewer.PrintMessage($"Invalid code length: {splitEndPoint[2]}", ConsoleColor.Red);
                        return false;
                    }

                    _endPoints = [.. addresses.Select(a => new IPEndPoint(a, port))];
                    _code = code;
                    return true;
                }
            }
        }
        else if (TransferMode == TransferMode.None)
        {
            FTViewer.PrintMessage("You should not send p2p and download at the same time.", ConsoleColor.Red);
            return false;
        }

        return true;
    }

    private static bool ParsePath(List<string> parsedArgs)
    {
        string? pathArg = parsedArgs
            .LastOrDefault();

        if (string.IsNullOrEmpty(pathArg) || !pathArg.Equals(Config.PathArgument))
        {
            FTViewer.PrintMessage("Path argument not found or not been last.", ConsoleColor.Red);
            return false;
        }

        return true;
    }
}