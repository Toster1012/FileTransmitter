using System.Diagnostics;
using System.Net;

namespace FileTransmitter;

internal sealed class FTServer
{
    public const int HttpPort = 8080;
    private const int MaxBufferSize = 81920;
    private const int MaxMegabytesPerSecond = 5;

    private readonly HttpListener _listener = new();

    private CancellationTokenSource? _token;

    public bool Start(string path, bool isDownload)
    {
        if (!PathIsValid(path))
            return false;

        _token = new();

        _listener.Prefixes.Add($"http://+:{HttpPort}/");
        _listener.Start();

        _ = Task.Run(() => ServerRoutine(path, isDownload, _token.Token));

        return true;
    }

    public void Stop()
    {
        try
        {
            _listener.Close();
            _token?.Cancel();
        }
        catch { }
    }

    private async Task ServerRoutine(string path, bool isDownload, CancellationToken token)
    {
        try
        {
            while (_listener.IsListening && !token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();

                _ = Task.Run(() => ProccesUser(context, path, isDownload, token), token);
            }
        }
        catch (HttpListenerException)
        {

        }
    }

    private async Task ProccesUser(HttpListenerContext context, string path, bool isDownload, CancellationToken token)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            long totalLength = fileInfo.Length;
            long start = 0;
            long end = totalLength - 1;
            bool isRangeRequest = false;
            string? rangeHeader = context.Request.Headers["Range"];

            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
            {
                string[] range = rangeHeader[6..].Split('-');
                start = long.Parse(range[0]);

                if (range.Length > 1 && !string.IsNullOrEmpty(range[1]))
                    end = long.Parse(range[1]);

                isRangeRequest = true;
            }

            long contentLength = end - start + 1;

            if (isDownload)
            {
                context.Response.ContentType = "application/octet-stream";
                context.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(fileInfo.Name)}\"");
            }

            context.Response.Headers.Add("Accept-Ranges", "bytes");

            if (isRangeRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                context.Response.Headers.Add("Content-Range", $"bytes {start}-{end}/{totalLength}");
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
            }

            context.Response.ContentLength64 = contentLength;

            using var fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var networkStream = context.Response.OutputStream;

            fileStream.Position = start;

            byte[] buffer = new byte[81920];
            long bytesRemaining = contentLength;
            int bytesRead;

            long maxBytesPerSecond = MaxMegabytesPerSecond * 1024 * 1024;
            var throttleTimer = new Stopwatch();

            while (bytesRemaining > 0 && (bytesRead = await fileStream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, bytesRemaining), token)) > 0)
            {
                throttleTimer.Restart();

                await networkStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                bytesRemaining -= bytesRead;

                throttleTimer.Stop();

                if (maxBytesPerSecond > 0)
                {
                    double targetMilliseconds = (bytesRead / (double)maxBytesPerSecond) * 1000;

                    int delayTime = (int)(targetMilliseconds - throttleTimer.ElapsedMilliseconds);

                    if (delayTime > 0)
                        await Task.Delay(delayTime, token);
                }
            }
        }
        catch { }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch { }
        }
    }

    private static bool PathIsValid(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (!File.Exists(path))
            return false;

        return true;
    }
}