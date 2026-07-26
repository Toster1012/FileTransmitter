using Open.Nat;

namespace FileTransmitter;

internal sealed class PortForwarder
{
    private const string MappingDescription = "FileTransmitter P2P";
    private const int DiscoveryTimeoutMs = 5000;

    private NatDevice? _device;
    private int _mappedPort;

    public async Task<bool> TryMapPortAsync(int port, CancellationToken token)
    {
        try
        {
            var discoverer = new NatDiscoverer();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(DiscoveryTimeoutMs);

            _device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

            await _device.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, MappingDescription));

            _mappedPort = port;
            return true;
        }
        catch (NatDeviceNotFoundException)
        {
            return false;
        }
        catch (MappingException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task RemoveMappingAsync()
    {
        if (_device is null || _mappedPort == 0)
            return;

        try
        {
            await _device.DeletePortMapAsync(new Mapping(Protocol.Tcp, _mappedPort, _mappedPort, MappingDescription));
        }
        catch (MappingException)
        {
        }
        catch (NatDeviceNotFoundException)
        {
        }

        _device = null;
        _mappedPort = 0;
    }
}