# SocketStorm

SocketStorm is a lightweight C# library that implements WebSocket server as a wrapper for the built-in HttpListener.

## Features

- Simple API for creating WebSocket servers
- Built on top of C# standard HttpListener
- Lightweight with minimal dependencies
- Easy integration with existing .NET applications

## Installation

You can add SocketStorm to your project using NuGet:

```
// TODO: Add NuGet package installation instructions
```

## Quick Start

Creating a WebSocket server with SocketStorm is simple. Here's how to get started:

```csharp
using System.Text;
using SocketStorm.Server;

// Create a WebSocket server instance
// Parameters: host, port, path, data type, max connections, subprotocol
using WebSocketServer server = new(
    "*",                   // Host: * for any available interface
    24517,                 // Port: your chosen port number
    "/ws/stream",          // Path: endpoint path for the WebSocket
    WebSocketDataType.Text, // Data type: Text or Binary
    50,                    // Max connections: limit of concurrent connections
    "my-protocol-v1"       // Optional subprotocol
);

// Set up event handlers for WebSocket events
server.ConnectionOpened += (_, args) => {
    Console.WriteLine($"Connection opened. Session ID: {args.SessionId}");
};

server.ConnectionClosed += (_, args) => {
    Console.WriteLine($"Connection closed. Session ID: {args.SessionId}");
};

server.MessageReceived += (_, args) => {
    // Convert received message bytes to string (for Text WebSockets)
    var message = Encoding.UTF8.GetString(args.Data);
    Console.WriteLine($"Message received from {args.SessionId}: {message}");
    
    // Echo the message back to the client
    var response = Encoding.UTF8.GetBytes($"Echo: {message}");
    server.SendAsync(response, args.SessionId).GetAwaiter().GetResult();
};

server.ExceptionThrown += (_, args) => {
    Console.WriteLine($"Exception thrown: {args.Exception}");
};

// Start the server
await server.StartAsync();

Console.WriteLine("WebSocket server started. Press Ctrl+C to stop.");

// Set up graceful shutdown
CancellationTokenSource cts = new();
Console.CancelKeyPress += async (_, _) => {
    await server.StopAsync();
    server.Dispose();
    cts.Cancel();
};

// Keep the application running
while (!cts.IsCancellationRequested) {
    await Task.Delay(100);
}
```

### Client Connection Example (JavaScript)

Connect to your WebSocket server from a web client:

```javascript
// Create a WebSocket connection
const socket = new WebSocket('ws://localhost:24517/ws/stream', 'my-protocol-v1');

// Connection opened
socket.addEventListener('open', (event) => {
    console.log('Connected to the server');
    socket.send('Hello Server!');
});

// Listen for messages
socket.addEventListener('message', (event) => {
    console.log('Message from server:', event.data);
});

// Connection closed
socket.addEventListener('close', (event) => {
    console.log('Disconnected from the server');
});

// Error handling
socket.addEventListener('error', (event) => {
    console.error('WebSocket error:', event);
});
```

## Documentation

Detailed documentation is coming soon.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
