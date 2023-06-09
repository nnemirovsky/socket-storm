using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace SocketStorm.Server;

public sealed class WebSocketServer : IWebSocketServer
{
    private record Session(WebSocket WebSocket)
    {
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }

    private const int BufferInitialSize = 16 * 1024;
    private const int BufferGrowthFactor = 2;
    private const double BufferSizeThreshold = 0.75;

    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);
    private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

    private readonly string _host;
    private readonly int _port;
    private readonly int _maxSessionCount;
    private readonly string _path;
    private readonly string? _subProtocol;
    private readonly WebSocketMessageType _messageType;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private Func<HttpListenerContext, CancellationToken, Task<bool>>? _preHook;
    private bool _stopped;
    private bool _disposed;

    public event EventHandler<ConnectionOpenedEventArgs>? ConnectionOpened;
    public event EventHandler<ConnectionClosedEventArgs>? ConnectionClosed;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ExceptionThrownEventArgs>? ExceptionThrown;

    public bool IsListening
    {
        get
        {
            CheckDisposed();
            return !_stopped && _listener.IsListening;
        }
    }

    public int ConnectedSessionCount
    {
        get
        {
            CheckDisposed();
            return _sessions.Count(s => s.Value.WebSocket.State == WebSocketState.Open);
        }
    }

    public WebSocketServer(
        DnsEndPoint endPoint,
        string path,
        WebSocketDataType dataType,
        int maxSessionCount = 50,
        string? subProtocol = null
    ) : this(endPoint.Host, endPoint.Port, path, dataType, maxSessionCount, subProtocol) { }

    public WebSocketServer(
        string host,
        int port,
        string path,
        WebSocketDataType dataType,
        int maxSessionCount = 50,
        string? subProtocol = null
    )
    {
        _host = host;
        _port = port;
        _path = path.Trim('/');
        _maxSessionCount = maxSessionCount;
        _subProtocol = subProtocol;
        _messageType = dataType switch
        {
            WebSocketDataType.Binary => WebSocketMessageType.Binary,
            WebSocketDataType.Text => WebSocketMessageType.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Invalid data type")
        };

        _listener.Prefixes.Add($"http://{_host}:{_port}/{_path}/");
    }

    public void AddHttpPreHook(Func<HttpListenerContext, CancellationToken, Task<bool>> callback)
    {
        CheckDisposed();

        _preHook = callback;
    }

    public bool IsConnected(Guid sessionId)
    {
        CheckDisposed();

        return _listener.IsListening &&
               _sessions.TryGetValue(sessionId, out var session) && session.WebSocket.State == WebSocketState.Open;
    }

    public Task StartAsync()
    {
        CheckDisposed();

        _listener.Start();

        if (!_listener.IsListening) throw new WebSocketException("Server failed to start");

        Task.Run(async () => await ListenAsync().ConfigureAwait(false), _cts.Token)
            .ContinueWith(t => ExceptionThrown?.Invoke(this, new(t.Exception!)), TaskContinuationOptions.OnlyOnFaulted);

        return Task.CompletedTask;
    }

    private async Task ListenAsync()
    {
        CheckDisposed();

        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext httpContext;
            try
            {
                httpContext = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                continue;
            }

            if (httpContext.Request.IsWebSocketRequest)
            {
                if (_sessions.Count >= _maxSessionCount)
                {
                    await HandleExtraRequest(httpContext).ConfigureAwait(false);
                    continue;
                }

                #pragma warning disable CS4014
                Task.Run(async () => await HandleConnectionAsync(httpContext).ConfigureAwait(false), _cts.Token)
                    .ContinueWith(
                        t => ExceptionThrown?.Invoke(this, new(t.Exception!)),
                        TaskContinuationOptions.OnlyOnFaulted
                    );
                #pragma warning restore CS4014
            }
            else
            {
                await HandleHttpRequest(httpContext).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleUnsupportedProtocol(HttpListenerContext httpContext)
    {
        httpContext.Response.StatusCode = (int) HttpStatusCode.BadRequest;
        httpContext.Response.ContentType = "text/plain";
        await httpContext.Response.OutputStream
            .WriteAsync("Unsupported protocol"u8.ToArray(), _cts.Token)
            .ConfigureAwait(false);
        httpContext.Response.Close();
    }

    private async Task HandleExtraRequest(HttpListenerContext httpContext)
    {
        httpContext.Response.StatusCode = (int) HttpStatusCode.TooManyRequests;
        httpContext.Response.ContentType = "text/plain";
        await httpContext.Response.OutputStream
            .WriteAsync("Too many sessions"u8.ToArray(), _cts.Token)
            .ConfigureAwait(false);
        httpContext.Response.Close();
    }

    private async Task HandleHttpRequest(HttpListenerContext httpContext)
    {
        httpContext.Response.StatusCode = (int) HttpStatusCode.BadRequest;
        httpContext.Response.ContentType = "text/plain";
        await httpContext.Response.OutputStream
            .WriteAsync("Only WebSocket connections are allowed"u8.ToArray(), _cts.Token)
            .ConfigureAwait(false);
        httpContext.Response.Close();
    }

    private async Task HandleConnectionAsync(HttpListenerContext httpContext)
    {
        if (_preHook is not null && await _preHook(httpContext, _cts.Token).ConfigureAwait(false) is false) return;

        WebSocketContext wsContext;
        try
        {
            wsContext = await httpContext.AcceptWebSocketAsync(_subProtocol, KeepAliveInterval).ConfigureAwait(false);
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.UnsupportedProtocol)
        {
            await HandleUnsupportedProtocol(httpContext).ConfigureAwait(false);
            return;
        }

        var sessionId = Guid.NewGuid();
        var webSocket = wsContext.WebSocket;
        _sessions[sessionId] = new(webSocket);
        ConnectionOpened?.Invoke(this, new(sessionId, httpContext.Request.RemoteEndPoint));

        var buffer = ArrayPool.Rent(BufferInitialSize);
        var currentIdx = 0;
        try
        {
            while (webSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                ResizeBufferIfNecessary(ref buffer, currentIdx);

                var segment = new ArraySegment<byte>(buffer, currentIdx, buffer.Length - currentIdx);
                var receiveResult = await webSocket.ReceiveAsync(segment, _cts.Token).ConfigureAwait(false);
                if (receiveResult.MessageType == _messageType)
                {
                    currentIdx += receiveResult.Count;

                    if (!receiveResult.EndOfMessage) continue;

                    MessageReceived?.Invoke(this, new(buffer[..currentIdx], sessionId));

                    currentIdx = 0;
                    ResetBufferIfNecessary(ref buffer);
                }
                else if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket
                        .CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", _cts.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            ConnectionClosed?.Invoke(this, new(sessionId));
            ArrayPool.Return(buffer);
            webSocket.Dispose();
            _sessions.TryRemove(sessionId, out _);
        }
    }

    private static void ResizeBufferIfNecessary(ref byte[] buffer, int currentIdx)
    {
        if (currentIdx >= buffer.Length * BufferSizeThreshold)
        {
            var newBuffer = ArrayPool.Rent(buffer.Length * BufferGrowthFactor);
            Array.Copy(buffer, newBuffer, buffer.Length);
            ArrayPool.Return(buffer);
            buffer = newBuffer;
        }
    }

    private static void ResetBufferIfNecessary(ref byte[] buffer)
    {
        if (buffer.Length > BufferInitialSize)
        {
            ArrayPool.Return(buffer);
            buffer = ArrayPool.Rent(BufferInitialSize);
        }
    }

    public async Task StopAsync()
    {
        CheckDisposed();

        if (_stopped) return;

        Task CloseWsUnderLockAsync(Session session) =>
            ExecuteUnderSendLockAsync(
                session,
                s => s.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopped", _cts.Token)
            );

        await _sessions
            .Select(s => Task.Run(() => CloseWsUnderLockAsync(s.Value).ConfigureAwait(false), _cts.Token))
            .WhenAll()
            .ConfigureAwait(false);

        _sessions.Clear();
        _listener.Stop();
        _listener.Close();
        _cts.Cancel();
        _stopped = true;
    }

    public async Task SendAsync(byte[] message, Guid sessionId)
    {
        CheckDisposed();

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new ArgumentException("Session not found", nameof(sessionId));
        }

        await ExecuteUnderSendLockAsync(session, s => s.SendAsync(message, _messageType, true, _cts.Token))
            .ConfigureAwait(false);
    }

    private async Task ExecuteUnderSendLockAsync(Session session, Func<WebSocket, Task> action)
    {
        await session.SendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            if (session.WebSocket.State == WebSocketState.Open)
            {
                await action(session.WebSocket).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("WebSocket is not open");
            }
        }
        finally
        {
            session.SendLock.Release();
        }
    }

    public async Task BroadcastAsync(byte[] message)
    {
        CheckDisposed();

        await _sessions
            .Select(s => Task.Run(() => Task.FromResult(SendAsync(message, s.Key).ConfigureAwait(false))))
            .WhenAll()
            .ConfigureAwait(false);
    }

    private void CheckDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        Task.Run(StopAsync).GetAwaiter().GetResult();
        _cts.Dispose();
        ((IDisposable) _listener).Dispose();
        _disposed = true;
    }
}
