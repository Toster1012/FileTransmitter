namespace FileTransmitter;

internal sealed class ArgumentParser
{
    private HashSet<string>? _arguments;
    private string? _path;

    public bool TryParse(string[] args)
    {
        return ParseArgument(args, out _arguments, out _path);
    }

    public string? Path => _path;
    public bool Help => IsSet(Config.HelpArgument);
    public bool Write => IsSet(Config.WriteArgument);
    public bool FastPack => IsSet(Config.FastPackArgument);

    private bool ParseArgument(string[] args, out HashSet<string>? parsedArgs, out string filePath)
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

        string? pathArg = parsedArgs
            .LastOrDefault();

        if (string.IsNullOrEmpty(pathArg) || !pathArg.Equals(Config.PathArgument))
        { 
            FTViewer.PrintMessage("Path argument not found or not been last.", ConsoleColor.Red);
            return false;
        }

        filePath = string.Join(' ', args
            .SkipWhile(a => a != Config.PathArgument)
            .Skip(1)
            .ToArray());

        return true;
    }

    private bool IsSet(string argument) => _arguments is not null && _arguments.Contains(argument);
}
