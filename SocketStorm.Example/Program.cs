using System.Text;
using SocketStorm.Server;

using WebSocketServer server = new("*", 24517, "/ws/stream", WebSocketDataType.Text, 2, "my-protocol-v1");
CancellationTokenSource cts = new();
server.ConnectionOpened += OnConnectionOpened;
server.ConnectionClosed += OnConnectionClosed;
server.MessageReceived += OnMessageReceived;
server.ExceptionThrown += OnExceptionThrown;

await server.StartAsync();

Console.CancelKeyPress += async (_, _) =>
{
    await server.StopAsync();
    server.Dispose();
    cts.Cancel();
};

while (!cts.IsCancellationRequested) { }


void OnConnectionOpened(object? _, ConnectionOpenedEventArgs args)
{
    Console.WriteLine($"Connection opened. Session id = {args.SessionId}, remote endpoint = {args.RemoteEndpoint}");
}

void OnConnectionClosed(object? _, ConnectionClosedEventArgs args)
{
    Console.WriteLine($"Connection closed. Session id = {args.SessionId}");
}

void OnMessageReceived(object? _, MessageReceivedEventArgs args)
{
    Console.WriteLine($"Message received from {args.SessionId}: {Encoding.UTF8.GetString(args.Data)}");
    server.SendAsync(Encoding.UTF8.GetBytes("echo: " + Encoding.UTF8.GetString(args.Data)), args.SessionId)
        .GetAwaiter()
        .GetResult();
}

void OnExceptionThrown(object? _, ExceptionThrownEventArgs args)
{
    Console.WriteLine($"Exception thrown: {args.Exception}");
}
