using System.Net;

namespace SocketStorm;

public class ConnectionOpenedEventArgs : EventArgs
{
    public Guid SessionId { get; }
    public IPEndPoint RemoteEndpoint { get; }

    public ConnectionOpenedEventArgs(Guid sessionId, IPEndPoint remoteEndpoint)
    {
        SessionId = sessionId;
        RemoteEndpoint = remoteEndpoint;
    }
}
