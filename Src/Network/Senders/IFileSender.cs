internal interface IFileSender
{
    Task<bool> StartAsync(string path, bool isDownload, CancellationToken token);
    void Stop();
}