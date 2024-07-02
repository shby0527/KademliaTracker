using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
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

    private readonly KRouter _kRouter = new(nodeId, provider);

    private const int MAX_PACK_SIZE = 0x10000;

    private readonly ConcurrentDictionary<BigInteger, KRpcPackage> _packages = new();

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
                _packages.TryAdd(find.FormattedTransaction, find);
                _logger.LogDebug("send to HOST:{host}, IP: {ip}, port: {port}",
                    item.Key, address, item.Value);
                _socket.SendTo(find.Encode(), new IPEndPoint(address, item.Value));
            }
        }
    }

    private void BeginReceive()
    {
        _receivedEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        if (_socket.ReceiveFromAsync(_receivedEventArgs)) return;
        _logger.LogTrace("already received");
        ThreadPool.QueueUserWorkItem(_ => this.OnReceived(_socket, _receivedEventArgs));
    }


    private void OnReceived(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            _logger.LogInformation("error package ,next receive {s}, r: {r}",
                args.SocketError, args.RemoteEndPoint);
            this.BeginReceive();
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
                _packages.TryGetValue(package.FormattedTransaction, out var request))
            {
                _packages.Remove(request.FormattedTransaction, out _);
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
                    package.FormattedTransaction);
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
        sender._logger.LogTrace("begin process find_node, transaction: {tr}", request.FormattedTransaction);
        var dictionary = response.Response;
        // found node response
        ReadOnlySpan<byte> id = (byte[])dictionary["id"];
        if (!sender._kRouter.HasNodeExists(id))
        {
            // first, add k-bucket this first node for
            var distance = KBucket.ComputeDistances(id, sender.CLIENT_NODE_ID.Span);
            sender._kRouter.AddNode(new NodeInfo
            {
                NodeID = id.ToArray(),
                Distance = distance,
                NodeAddress = ip.Address,
                NodePort = ip.Port,
                LatestAccessTime = DateTimeOffset.Now
            });
        }

        ReadOnlySpan<byte> nodes = (byte[])dictionary["nodes"];
        var nodeCount = nodes.Length / 26;
        sender._logger.LogTrace("found {count} nodes", nodeCount);
        var latestPrefixLength = sender._kRouter.LatestPrefixLength;
        for (var i = 0; i < nodes.Length; i += 26)
        {
            var item = nodes[i..(i + 26)];
            // node id is
            var itemId = item[..20];
            var itemIP = new IPAddress(item[20..24]);
            var port = (int)new BigInteger(item[24..26], true, true);
            sender._logger.LogTrace("received node ID:{id}, IP: {ip}, port: {port} ",
                BitConverter.ToString(itemId.ToArray()).Replace("-", ""), itemIP, port);
            if (itemId.SequenceEqual(sender.CLIENT_NODE_ID.Span))
            {
                sender._logger.LogTrace("found my self, stoped");
                return;
            }

            if (sender._kRouter.HasNodeExists(itemId))
            {
                sender._logger.LogTrace("node has already in this");
                continue;
            }

            var node = new NodeInfo
            {
                NodeID = itemId.ToArray(),
                Distance = KBucket.ComputeDistances(itemId, sender.CLIENT_NODE_ID.Span),
                NodeAddress = itemIP,
                NodePort = port,
                LatestAccessTime = DateTimeOffset.Now
            };
            var prefixLength = sender._kRouter.AddNode(node);
            sender._logger.LogInformation("now node count: {c}", sender._kRouter.PeersCount);
            if ((prefixLength > latestPrefixLength || prefixLength < 20) && node.Distance > BigInteger.Zero)
            {
                sender.SendPackage(new IPEndPoint(itemIP, port),
                    KademliaProtocols.FindNode(sender.CLIENT_NODE_ID.Span, sender.CLIENT_NODE_ID.Span));
            }
        }
    }

    private void SendPackage(IPEndPoint endpoint, KRpcPackage package)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            _packages.TryAdd(package.FormattedTransaction, package);
            _logger.LogDebug("send to  IP: {ip}, port: {port}",
                endpoint.Address, endpoint.Port);
            _socket.SendTo(package.Encode(), endpoint);
        });
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