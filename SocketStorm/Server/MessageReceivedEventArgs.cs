namespace SocketStorm.Server;

public class MessageReceivedEventArgs : EventArgs
{
    public byte[] Data { get; }
    public Guid SessionId { get; }

    public MessageReceivedEventArgs(byte[] data, Guid sessionId)
    {
        Data = data;
        SessionId = sessionId;
    }
}
