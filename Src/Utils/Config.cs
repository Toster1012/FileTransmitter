namespace FileTransmitter;

internal static class Config
{
    public static string ZipFileExtension = ".zip";
    public static string ProjectVersion = "2.0.0";

    public static string HelpArgument = "--help";
    public static string WriteArgument = "-w";
    public static string FastPackArgument = "-f";
    public static string PathArgument = "-p";
    public static string SendP2P = "-n";
    public static string DownloadP2P = "-o";

    public static int P2pPort = 8081;
    public static int P2pCodeByteLength = 5;

    public static IReadOnlyList<string> StunServer =
    [
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302",
        "stun.cloudflare.com:3478"
    ];
}