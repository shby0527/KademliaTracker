using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
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

    private readonly ConcurrentStack<KBucket> _buckets = [];

    private const int MAX_PACK_SIZE = 0x10000;

    private readonly ConcurrentDictionary<string, KRpcPackage> _packages = new();

    private readonly ImmutableDictionary<string, Action<KademliaNode, KRpcPackage, KRpcPackage, EndPoint>> _eventMap
        = new Dictionary<string, Action<KademliaNode, KRpcPackage, KRpcPackage, EndPoint>>()
        {
            { "find_node", OnFindNodeResponse }
        }.ToImmutableDictionary();

    private Timer? _timer;

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
        _buckets.Push(new KBucket
        {
            BucketDistance = 0,
            Nodes = new ConcurrentQueue<NodeInfo>(),
            Split = false
        });
        ThreadPool.QueueUserWorkItem(_ => this.Initial());
        _timer = new Timer(_ => this.CheckPackageALive(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }


    private void Initial()
    {
        // begin query the initial node to find new node
        _logger.LogTrace("initial node from Bootstrap");
        foreach (var item in config.BootstrapList)
        {
            _logger.LogTrace("bootstrap item is {item}", item);
            // 用find_node 先请求一下?
            var addresses = Dns.GetHostAddresses(item.Key, AddressFamily.InterNetwork).Distinct();
            foreach (var address in addresses)
            {
                var find = KademliaProtocols.FindNode(CLIENT_NODE_ID.Span, CLIENT_NODE_ID.Span);
                _packages.TryAdd(find.TransactionId, find);
                _logger.LogDebug("send to HOST:{host}, IP: {ip}, port: {port}",
                    item.Key, address, item.Value);
                _socket.SendTo(find.Encode(), new IPEndPoint(address, item.Value));
            }
        }
    }

    private void BeginReceive()
    {
        _receivedEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        if (!_socket.ReceiveFromAsync(_receivedEventArgs))
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

        var buffered = args.MemoryBuffer[..args.BytesTransferred];
        try
        {
            var package = KRpcPackage.Decode(buffered.Span);
            _logger.LogTrace("received package {page}， Type: {type}, from {from}",
                package, package.Type, args.RemoteEndPoint);
            // found request package
            if (package is { Type: KRpcTypes.Response } &&
                _packages.TryGetValue(package.TransactionId, out var request))
            {
                _packages.Remove(request.TransactionId, out _);
                if (_eventMap.TryGetValue(request.Query?.Method ?? "", out var action))
                {
                    action(this, request, package, args.RemoteEndPoint!);
                }
                else
                {
                    _logger.LogTrace("unsupported operator");
                }
            }
            else
            {
                _logger.LogDebug("unable found request package, maybe time out, transaction {t}",
                    package.TransactionId);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "error parse and process package");
        }

        // receive next package
        this.BeginReceive();
    }


    private static void OnFindNodeResponse(KademliaNode sender,
        KRpcPackage request, KRpcPackage response,
        EndPoint remote)
    {
        if (response.Response == null) return;
        if (remote is not IPEndPoint ip) return;
        sender._logger.LogTrace("begin process find_node, transaction: {tr}", request.TransactionId);
        var dictionary = response.Response;
        // found node response
        ReadOnlySpan<byte> id = Encoding.ASCII.GetBytes((string)dictionary["id"]);
        // first, add k-bucket this first node for
        var distance = KBucket.ComputeDistances(id, sender.CLIENT_NODE_ID.Span);
        if (sender._buckets.Count > distance)
        {
            var buck = (from p in sender._buckets
                where p.BucketDistance == distance
                select p).FirstOrDefault();
            if (buck == null) return;
            buck.Nodes.Enqueue(new NodeInfo
            {
                NodeID = id.ToArray(),
                Distance = distance,
                NodeAddress = ip.Address,
                NodePort = ip.Port,
                LatestAccessTime = DateTimeOffset.Now
            });
        }
        else
        {
            if (sender._buckets.TryPeek(out var bucket))
            {
                bucket.Nodes.Enqueue(new NodeInfo
                {
                    NodeID = id.ToArray(),
                    Distance = distance,
                    NodeAddress = ip.Address,
                    NodePort = ip.Port,
                    LatestAccessTime = DateTimeOffset.Now
                });
            }
        }

        ReadOnlySpan<byte> nodes = Encoding.ASCII.GetBytes((string)dictionary["nodes"]);
    }


    private void CheckPackageALive()
    {
        var now = DateTimeOffset.Now;
        foreach (var package in _packages)
        {
            var l = now - package.Value.CreateTime;
            if (l < TimeSpan.FromSeconds(10)) continue;
            _logger.LogTrace("found {t} timeout , remove it", package.Key);
            _packages.Remove(package.Key, out _);
        }
    }

    public void Stop()
    {
        _logger.LogTrace("Node Stopping");
        _receivedEventArgs.Completed -= this.OnReceived;
        _receivedEventArgs.Dispose();
        _socket.Close();
        _socket.Dispose();
        _timer?.Dispose();
    }
}