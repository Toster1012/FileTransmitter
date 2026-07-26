namespace FileTransmitter;

internal sealed class P2pSender : IFileSender
{
    private readonly P2pListener _listener = new();

    public string ConnectionString => _listener.ConnectionString;

    public Task<bool> StartAsync(string path, bool isDownload, CancellationToken token)
    {
        if (!File.Exists(path))
            return Task.FromResult(false);

        return _listener.StartAsync((stream, session, ct) => P2pFileTransfer.SendFileAsync(stream, session, path, ct), token);
    }

    public void Stop() => _listener.Stop();
}
