namespace FileTransmitter;

internal sealed class P2pReceiveListener
{
    private readonly P2pListener _listener = new();
    private readonly string _saveDirectory;

    public string ConnectionString => _listener.ConnectionString;

    public P2pReceiveListener(string saveDirectory)
    {
        _saveDirectory = saveDirectory;
    }

    public Task<bool> StartAsync(CancellationToken token)
    {
        return _listener.StartAsync((stream, session, ct) => P2pFileTransfer.ReceiveFileAsync(stream, session, _saveDirectory, ct), token);
    }

    public void Stop() => _listener.Stop();
}
