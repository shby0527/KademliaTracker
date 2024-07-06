using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    private readonly IHostEnvironment _environment = provider.GetRequiredService<IHostEnvironment>();

    private const int MAX_PACK_SIZE = 0x10000;

    private readonly ConcurrentDictionary<BigInteger, KRpcPackage> _packages = new();

    private readonly BitTorrentInfoHashManager _torrentInfoHashManager = new(provider);

    private readonly ImmutableDictionary<string, Action<KademliaNode, KRpcPackage, KRpcPackage, EndPoint>> _eventMap
        = new Dictionary<string, Action<KademliaNode, KRpcPackage, KRpcPackage, EndPoint>>()
        {
            { "find_node", OnFindNodeResponse },
            { "ping", OnPingResponse },
            { "get_peers", OnGetPeersResponse }
        }.ToImmutableDictionary();


    private readonly ImmutableDictionary<string, Action<KademliaNode, KRpcPackage, EndPoint>> _eventRequestMap
        = new Dictionary<string, Action<KademliaNode, KRpcPackage, EndPoint>>()
        {
            { "get_peers", OnGetPeersRequest },
            { "announce_peer", OnAnnouncePeerRequest },
            { "ping", OnPingRequest },
            { "find_node", OnFindNodeRequest }
        }.ToImmutableDictionary();

    private Timer? _timer;


    private readonly Semaphore _fileWrite = new(1, 1);

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

            switch (package)
            {
                // found request package
                case { Type: KRpcTypes.Response } when
                    _packages.TryGetValue(package.FormattedTransaction, out var request):
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

                    break;
                }
                case { Type: KRpcTypes.Query, Query: not null }:
                    if (_eventRequestMap.TryGetValue(package.Query.Value.Method, out var requestProcess))
                    {
                        requestProcess(this, package, args.RemoteEndPoint!);
                    }
                    else
                    {
                        _logger.LogTrace("unsupported request operator");
                    }

                    break;
                default:
                    _logger.LogDebug("unable found request package, maybe time out, transaction {t}",
                        package.FormattedTransaction);
                    break;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "error parse and process package");
        }

        // receive next package
        this.BeginReceive();
    }

    private static void OnGetPeersResponse(KademliaNode sender,
        KRpcPackage request, KRpcPackage response,
        EndPoint remote)
    {
        var logger = sender._logger;
        logger.LogTrace("received get peers response");
        if (response.Response == null) return;
        if (remote is not IPEndPoint ip) return;
        var dictionary = response.Response;
        ReadOnlySpan<byte> nodeId = (byte[])dictionary["id"];
        if (sender._kRouter.TryFoundNode(nodeId, out var node))
        {
            sender._kRouter.AdjustNode(node);
        }

        if (dictionary.TryGetValue("nodes", out var nodes))
        {
            ReadOnlySpan<byte> buffer = (byte[])nodes;
            for (var i = 0; i < buffer.Length; i += 26)
            {
                var item = buffer[i..(i + 26)];
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

                if (!sender._kRouter.TryFoundNode(itemId, out var n))
                {
                    sender._logger.LogTrace("node not exists in this");
                    // send ping
                    var ping = KademliaProtocols.Ping(sender.CLIENT_NODE_ID.Span);
                    sender.SendPackage(new IPEndPoint(itemIP, port), ping);
                    n = new NodeInfo
                    {
                        NodeID = itemId.ToArray(),
                        Distance = KBucket.ComputeDistances(itemId, sender.CLIENT_NODE_ID.Span),
                        NodeAddress = itemIP,
                        NodePort = port,
                        LatestAccessTime = DateTimeOffset.Now
                    };
                    var prefixLength = sender._kRouter.AddNode(n);
                    logger.LogTrace("this node prefix length {l}", prefixLength);
                }

                logger.LogTrace("add info hash");
                ReadOnlySpan<byte> btih = (byte[])request.Query!.Value.Arguments["info_hash"];
                var torrentInfoHash = sender._torrentInfoHashManager.AddBitTorrentInfoHash(btih);
                logger.LogTrace("current node dist {dist}", torrentInfoHash.MaxDistance);
                if (!torrentInfoHash.AnnounceNode(n)) continue;
                logger.LogTrace("find get peers {btih}", torrentInfoHash.HashText);
                var package = KademliaProtocols.GetPeersRequest(sender.CLIENT_NODE_ID.Span, torrentInfoHash.Hash);
                sender.SendPackage(new IPEndPoint(n.NodeAddress, n.NodePort), package);
            }
        }

        if (!dictionary.TryGetValue("values", out var obj) || obj is not ICollection<object> peers) return;
        {
            logger.LogTrace("found peers, count {c}", peers.Count);
            ReadOnlySpan<byte> btih = (byte[])request.Query!.Value.Arguments["info_hash"];
            var torrentInfoHash = sender._torrentInfoHashManager.AddBitTorrentInfoHash(btih);
            logger.LogTrace("current node dist {dist}", torrentInfoHash.MaxDistance);
            List<IPeer> p = [];
            foreach (var item in peers)
            {
                if (item is not byte[] pip) continue;
                ReadOnlySpan<byte> peerData = pip;
                var address = new IPAddress(peerData[..4]);
                var port = (int)new BigInteger(peerData[4..6], true, true);
                p.Add(BitTorrentInfoHashManager.CreatePeer(address, port, node!));
            }

            torrentInfoHash.AddPeers(p);
            torrentInfoHash.BeginGetMetadata();
        }
    }

    private static void OnPingResponse(KademliaNode sender,
        KRpcPackage request, KRpcPackage response,
        EndPoint remote)
    {
        var logger = sender._logger;
        logger.LogTrace("received ping response");
        if (response.Response == null) return;
        if (remote is not IPEndPoint ip) return;
        var dictionary = response.Response;
        ReadOnlySpan<byte> nodeId = (byte[])dictionary["id"];
        if (sender._kRouter.TryFoundNode(nodeId, out var node))
        {
            sender._kRouter.AdjustNode(node);
        }
    }

    private static void OnGetPeersRequest(KademliaNode sender,
        KRpcPackage request, EndPoint remote)
    {
        var logger = sender._logger;
        logger.LogTrace("get peers request");
        if (remote is not IPEndPoint ip) return;
        // save btih
        var query = request.Query!.Value;
        ReadOnlySpan<byte> hash = (byte[])query.Arguments["info_hash"];
        //first try response get peers request
        var list = sender._kRouter.FindNodeList(hash);
        List<byte> nodes = [];
        foreach (var info in list)
        {
            // nodeid,
            nodes.AddRange(info.NodeID.Span);
            // ip
            nodes.AddRange(info.NodeAddress.GetAddressBytes());
            BigInteger port = info.NodePort;
            // port
            nodes.AddRange(port.ToByteArray(true, true));
        }

        var btih = BitConverter.ToString(hash.ToArray()).Replace("-", "");
        ICollection<byte[]> peers = [];
        if (sender._torrentInfoHashManager.TryGetBitTorrentInfoHash(btih, out var infoH))
        {
            peers = new List<byte[]>();
            foreach (var peer in infoH.Peers)
            {
                var bytes = peer.Address.GetAddressBytes();
                var port = new BigInteger(peer.Port).ToByteArray(true, true);
                peers.Add([..bytes, ..port]);
            }
        }

        var response = KademliaProtocols.GetPeersResponse(sender.CLIENT_NODE_ID.Span, nodes.ToArray(),
            peers, request.TransactionId);
        sender.SendPackage(ip, response);
        if(peers.Count == 0){
            logger.LogTrace("begin auto send get peers for {hash}", btih);
            sender.SendGetPeers(hash);
        }
        var semaphore = sender._fileWrite;
        var magnetLink = $"magnet:?xt=urn:btih:{BitConverter.ToString(hash.ToArray()).Replace("-", "")}";
        logger.LogTrace("found magnet link {link}", magnetLink);
        try
        {
            semaphore.WaitOne();
            var info = sender._environment.ContentRootFileProvider.GetFileInfo("bt/magnet.txt");
            var outfile = info.PhysicalPath!;
            FileInfo fileInfo = new(outfile);
            if (!(fileInfo.Directory?.Exists ?? true))
            {
                fileInfo.Directory?.Create();
            }

            File.AppendAllLines(outfile, [magnetLink]);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static void OnAnnouncePeerRequest(KademliaNode sender,
        KRpcPackage request, EndPoint remote)
    {
        var logger = sender._logger;
        logger.LogTrace("announce peer request");
        if (remote is not IPEndPoint ip) return;
        var arguments = request.Query!.Value.Arguments;
        ReadOnlySpan<byte> id = (byte[])arguments["id"];
        if (sender._kRouter.TryFoundNode(id, out var node))
        {
            sender._kRouter.AdjustNode(node);
        }

        ReadOnlySpan<byte> hash = (byte[])arguments["info_hash"];
        var infoHash = sender._torrentInfoHashManager.AddBitTorrentInfoHash(hash);
        if (arguments.TryGetValue("implied_port", out var iPort) && iPort is int p && p != 0)
        {
            var peer = BitTorrentInfoHashManager.CreatePeer(ip.Address, ip.Port, node!);
            infoHash.AddPeers([peer]);
        }
        else
        {
            var eport = (int)arguments["port"];
            var peer = BitTorrentInfoHashManager.CreatePeer(ip.Address, eport, node!);
            infoHash.AddPeers([peer]);
        }

        infoHash.AnnounceNode(node!);

        var semaphore = sender._fileWrite;
        var magnetLink = $"magnet:?xt=urn:btih:{BitConverter.ToString(hash.ToArray()).Replace("-", "")}";
        logger.LogTrace("found magnet link {link}", magnetLink);
        try
        {
            semaphore.WaitOne();
            var info = sender._environment.ContentRootFileProvider.GetFileInfo("bt/magnet.txt");
            var outfile = info.PhysicalPath!;
            FileInfo fileInfo = new(outfile);
            if (!(fileInfo.Directory?.Exists ?? true))
            {
                fileInfo.Directory?.Create();
            }

            File.AppendAllLines(outfile, [magnetLink]);
        }
        finally
        {
            semaphore.Release();
        }

        logger.LogTrace("announce response, transaction {tr}", request.FormattedTransaction);
        sender.SendPackage(ip, KademliaProtocols.PingResponse(sender.CLIENT_NODE_ID.Span, request.TransactionId));
    }

    private static void OnFindNodeRequest(KademliaNode sender,
        KRpcPackage request, EndPoint remote)
    {
        var logger = sender._logger;
        logger.LogTrace("find node request");
        if (remote is not IPEndPoint ip) return;
        var arguments = request.Query!.Value.Arguments;
        ReadOnlySpan<byte> nodeId = (byte[])arguments["id"];
        ReadOnlySpan<byte> target = (byte[])arguments["target"];
        if (sender._kRouter.TryFoundNode(nodeId, out var node))
        {
            sender._kRouter.AdjustNode(node);
        }
        else
        {
            sender._kRouter.AddNode(new NodeInfo
            {
                NodeID = nodeId.ToArray(),
                Distance = KBucket.ComputeDistances(nodeId, sender.CLIENT_NODE_ID.Span),
                NodeAddress = ip.Address,
                NodePort = ip.Port,
                LatestAccessTime = DateTimeOffset.Now
            });
        }

        // find node request, response distance max top 8
        var list = sender._kRouter.FindNodeList(target);
        List<byte> nodes = [];
        foreach (var info in list)
        {
            // nodeid,
            nodes.AddRange(info.NodeID.Span);
            // ip
            nodes.AddRange(info.NodeAddress.GetAddressBytes());
            BigInteger port = info.NodePort;
            // port
            nodes.AddRange(port.ToByteArray(true, true));
        }

        sender.SendPackage(ip,
            KademliaProtocols.FindNodeResponse(sender.CLIENT_NODE_ID.Span,
                nodes.ToArray(), request.TransactionId));
    }

    private static void OnPingRequest(KademliaNode sender,
        KRpcPackage request, EndPoint remote)
    {
        var logger = sender._logger;
        logger.LogTrace("ping request");
        if (remote is not IPEndPoint ip) return;
        var arguments = request.Query!.Value.Arguments;
        ReadOnlySpan<byte> id = (byte[])arguments["id"];
        if (sender._kRouter.TryFoundNode(id, out var node))
        {
            sender._kRouter.AdjustNode(node);
        }

        logger.LogTrace("ping response, transaction {tr}", request.FormattedTransaction);
        sender.SendPackage(ip, KademliaProtocols.PingResponse(sender.CLIENT_NODE_ID.Span, request.TransactionId));
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
            if ((prefixLength > latestPrefixLength || prefixLength < 40)
                && sender._kRouter.PeersCount <= 15000
                && node.Distance > BigInteger.Zero)
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
            try
            {
                if (package.Type == KRpcTypes.Query)
                {
                    _packages.TryAdd(package.FormattedTransaction, package);
                }

                _logger.LogDebug("send to  IP: {ip}, port: {port}",
                    endpoint.Address, endpoint.Port);
                _socket.SendTo(package.Encode(), endpoint);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error send message");
            }
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


    public void SendGetPeers(ReadOnlySpan<byte> infoHash)
    {
        // 计算距离
        var distances = KBucket.ComputeDistances(infoHash, CLIENT_NODE_ID.Span);
        var bucket = _kRouter.FindNestDistanceBucket(KBucket.PrefixLength(distances));
        // 找到top8
        var infos = bucket.Nodes.Take(8);
        foreach (var info in infos)
        {
            var request = KademliaProtocols.GetPeersRequest(CLIENT_NODE_ID.Span, infoHash);
            SendPackage(new IPEndPoint(info.NodeAddress, info.NodePort), request);
        }
    }


    public string GetNodeCount()
    {
        return _kRouter.PeersCount.ToString();
    }

    public string GetBucketCount()
    {
        return _kRouter.BucketCount;
    }

    public string ListBitTorrentInfoHash()
    {
        StringBuilder sb = new();
        foreach (var item in _torrentInfoHashManager)
        {
            sb.Append($"{item.HashText}: peers count {item.Peers.Count.ToString()}\r\n");
        }

        return sb.ToString();
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