using System.Net;
using System.Net.Sockets;
using System.Text;
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

    private readonly KBucket[] _buckets = new KBucket[160];

    private const int MAX_PACK_SIZE = 0x10000;

    private const int MAX_SPLIT_NODE = 20;

    private int _splitedBucket = 0;

    public void Start()
    {
        _logger.LogTrace("Starting Node in port {port}", config.Port);
        Memory<byte> buffer = new byte[MAX_PACK_SIZE];
        _receivedEventArgs.SetBuffer(buffer);
        _receivedEventArgs.Completed += this.OnReceived;
        var endpoint = new IPEndPoint(IPAddress.Any, config.Port);
        _socket.Bind(endpoint);
        this.BeginReceive();
        _logger.LogTrace("initial KBucket");
        _buckets[0] = new KBucket
        {
            BucketDistance = 0,
            Nodes = new Queue<NodeInfo>(),
            Split = false
        };
        ThreadPool.QueueUserWorkItem(_ => this.Initial());
    }


    private void Initial()
    {
        KRpcPackage package = new()
        {
            TransactionId = "cadga",
            Type = KRpcTypes.Query,
            Query = new QueryPackage
            {
                Method = "ping",
                Arguments = new Dictionary<string, object>
                {
                    { "id", "cadsgfagracasdf" }
                }
            }
        };
        var encode = package.Encode();
        _logger.LogTrace("test package encoded {e}", Encoding.UTF8.GetString(encode));
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

        // receive next package
        this.BeginReceive();
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