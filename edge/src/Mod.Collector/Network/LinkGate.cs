namespace Mod.Collector.Network;

internal sealed class LinkGate
{
    private volatile bool _isUp = true;

    public bool IsUp => _isUp;

    public void SetLink(bool isUp) => _isUp = isUp;

    // Throws NetworkUnavailableException if the link is simulated down;
    // the request must not leave the process.
    public void Check()
    {
        if (!_isUp) throw new NetworkUnavailableException();
    }
}

internal sealed class NetworkUnavailableException()
    : Exception("Simulated link is down — request did not leave the process");
