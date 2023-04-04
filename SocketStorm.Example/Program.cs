using System.Text;
using SocketStorm.Server;

using WebSocketServer server = new("*", 24517, "/ws/stream", WebSocketDataType.Text, 2, "my-protocol-v1");

server.ConnectionOpened += (_, args) =>
    Console.WriteLine($"Connection opened. Session id = {args.SessionId}, remote endpoint = {args.RemoteEndpoint}");

server.ConnectionClosed += (_, args) => Console.WriteLine($"Connection closed. Session id = {args.SessionId}");

server.MessageReceived += async (_, args) =>
{
    Console.WriteLine($"Message received from {args.SessionId}: {Encoding.UTF8.GetString(args.Data)}");
    await server.SendAsync(Encoding.UTF8.GetBytes("echo: " + Encoding.UTF8.GetString(args.Data)), args.SessionId);
};

server.ExceptionThrown += (_, args) => throw args.Exception;

await server.StartAsync();

Console.CancelKeyPress += async (_, _) =>
{
    await server.StopAsync();
    server.Dispose();
    Environment.Exit(0);
};

while (true) await Task.Delay(500);
