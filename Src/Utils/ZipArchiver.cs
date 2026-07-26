using System.IO.Compression;

namespace FileTransmitter;

internal static class ZipArchiver
{
    public static async Task<(bool, string)> TryPackDirectory(string path, bool isFasted)
    {
        var zipArchivePath = string.Empty;

        if (!Directory.Exists(path))
            return (false, string.Empty);

        if (!TryGetZipFileName(path, out zipArchivePath))
            return (false, string.Empty);

        if (File.Exists(zipArchivePath))
            File.Delete(zipArchivePath);

        var token = new CancellationTokenSource();
        var animationTask = Task.Run(() => FTViewer.ShowAnimation(token.Token));
        var compressionLevel = isFasted ? CompressionLevel.Fastest : CompressionLevel.Optimal;

        await ZipFile.CreateFromDirectoryAsync(path, zipArchivePath, compressionLevel, includeBaseDirectory: true);

        token.Cancel();

        try
        {
            await animationTask;
        }
        catch (OperationCanceledException) { }

        return (File.Exists(zipArchivePath), zipArchivePath);
    }

    private static bool TryGetZipFileName(string directoryPath, out string zipFilePath)
    {
        zipFilePath = string.Empty;

        if (!Directory.Exists(directoryPath))
            return false;

        var info = new DirectoryInfo(directoryPath);
        var fileName = $"{info.Name}{Config.ZipFileExtension}";
        var tempPath = Path.GetTempPath();

        zipFilePath = Path.Combine(tempPath, fileName);
        return true;
    }
}
