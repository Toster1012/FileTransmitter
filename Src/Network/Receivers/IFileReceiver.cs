namespace FileTransmitter;

internal interface IFileReceiver
{
    Task<bool> ReceiveAsync(CancellationToken token);
    void Stop();
}