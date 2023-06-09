namespace SocketStorm.Server;

public class ConnectionClosedEventArgs : EventArgs
{
    public Guid SessionId { get; }

    public ConnectionClosedEventArgs(Guid sessionId) => SessionId = sessionId;
}
