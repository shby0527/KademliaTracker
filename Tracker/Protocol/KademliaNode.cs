using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Configurations;

namespace Umi.Dht.Client.Protocol;

public class KademliaNode(ReadOnlyMemory<byte> nodeId, IServiceProvider provider, KademliaConfig config)
{
    private readonly ReadOnlyMemory<byte> CLIENT_NODE_ID = nodeId;

    private readonly ILogger<KademliaNode> _logger = provider.GetRequiredService<ILogger<KademliaNode>>();

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    private readonly SocketAsyncEventArgs _receivedEventArgs = new();

    private const int MAX_PACK_SIZE = 0x10000;

    public void Start()
    {
        _logger.LogTrace("Starting Node in port {port}", config.Port);
        Memory<byte> buffer = new byte[MAX_PACK_SIZE];
        _receivedEventArgs.SetBuffer(buffer);
        _receivedEventArgs.Completed += this.OnReceived;
        var endpoint = new IPEndPoint(IPAddress.Any, config.Port);
        _socket.Bind(endpoint);
        this.BeginReceive();
    }

    private void BeginReceive()
    {
        if (!_socket.ReceiveAsync(_receivedEventArgs))
        {
            this.OnReceived(_socket, _receivedEventArgs);
        }
    }


    private void OnReceived(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            _logger.LogInformation("close");
            return;
        }
    }

    public void Stop()
    {
        _logger.LogTrace("Node Stopping");
        _receivedEventArgs.Completed -= this.OnReceived;
        _receivedEventArgs.Dispose();
        _socket.Close();
        _socket.Dispose();
    }
}