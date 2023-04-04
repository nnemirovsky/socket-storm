using System.Net.WebSockets;
using System.Text;
using SocketStorm.Server;
using Xunit;
using Xunit.Abstractions;

namespace SocketStorm.Tests;

public class WebSocketServerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public WebSocketServerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task Receive_ShortMessage_Echo()
    {
        const string message = "hello 123\n\n    &*^";

        using WebSocketServer server = new(new("localhost", 23415), "/ws/test", WebSocketDataType.Text);
        server.ConnectionOpened += (_, _) => _testOutputHelper.WriteLine("Connection opened");
        server.ConnectionClosed += (_, _) => _testOutputHelper.WriteLine("Connection closed");
        server.MessageReceived += async (_, args) =>
        {
            Assert.Equal(message, Encoding.UTF8.GetString(args.Data));
            _testOutputHelper.WriteLine(
                $"Message received from {args.SessionId}: {Encoding.UTF8.GetString(args.Data)}"
            );

            await server.SendAsync(args.Data, args.SessionId);
        };
        server.ExceptionThrown += (_, args) => throw args.Exception;
        await server.StartAsync();

        using ClientWebSocket client = new();
        await client.ConnectAsync(new("ws://localhost:23415/ws/test/"), new CancellationTokenSource(1000).Token);
        if (client.State == WebSocketState.Open)
        {
            await client.SendAsync(
                Encoding.UTF8.GetBytes(message),
                WebSocketMessageType.Text,
                true,
                new CancellationTokenSource(1000).Token
            );

            var buffer = new byte[1024];
            var result = await client.ReceiveAsync(buffer, new CancellationTokenSource(1000).Token);
            var receivedMsg = Encoding.UTF8.GetString(buffer, 0, result.Count);

            _testOutputHelper.WriteLine(receivedMsg);
            Assert.Equal(message, receivedMsg);

            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", new CancellationTokenSource(1000).Token);
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        await server.StopAsync();
    }
}
