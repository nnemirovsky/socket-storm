using System.Net;

namespace SocketStorm;

public interface IWebSocketServer : IDisposable
{
    event EventHandler<ConnectionOpenedEventArgs>? ConnectionOpened;
    event EventHandler<ConnectionClosedEventArgs>? ConnectionClosed;
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ExceptionThrownEventArgs>? ExceptionThrown;

    bool IsListening { get; }
    int ConnectedSessionCount { get; }

    void AddHttpPreHook(Func<HttpListenerContext, CancellationToken, Task<bool>> callback);
    bool IsConnected(Guid sessionId);
    Task StartAsync();
    Task StopAsync();
    Task SendAsync(byte[] message, Guid sessionId);
    Task BroadcastAsync(byte[] message);
}
