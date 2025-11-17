using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Configurations;

namespace Umi.Dht.Client.Protocol;

public class KademliaNode
{
    private readonly ReadOnlyMemory<byte> CLIENT_NODE_ID;

    private readonly ILogger<KademliaNode> _logger;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    private readonly SocketAsyncEventArgs _receivedEventArgs = new();

    private readonly KRouter _kRouter;

    private readonly IHostEnvironment _environment;

    private const int MAX_PACK_SIZE = 0x10000;

    private readonly KademliaConfig _config;

    private readonly ConcurrentDictionary<BigInteger, KRpcPackage> _packages = new();

    private readonly BitTorrentInfoHashManager _torrentInfoHashManager;

    private readonly ImmutableDictionary<string, Action<KRpcPackage, KRpcPackage, EndPoint>> _eventMap;


    private readonly ImmutableDictionary<string, Action<KRpcPackage, EndPoint>> _eventRequestMap;

    private Timer? _timer;

    private readonly Semaphore _fileWrite = new(1, 1);

    public KademliaNode(ReadOnlyMemory<byte> nodeId, IServiceProvider provider, KademliaConfig config)
    {
        CLIENT_NODE_ID = nodeId;
        _config = config;
        _logger = provider.GetRequiredService<ILogger<KademliaNode>>();
        _kRouter = new KRouter(nodeId, provider);
        _environment = provider.GetRequiredService<IHostEnvironment>();
        _torrentInfoHashManager = new BitTorrentInfoHashManager(provider);
        _eventMap = new Dictionary<string, Action<KRpcPackage, KRpcPackage, EndPoint>>()
        {
            { "find_node", OnFindNodeResponse },
            { "ping", OnPingResponse },
            { "get_peers", OnGetPeersResponse }
        }.ToImmutableDictionary();
        _eventRequestMap = new Dictionary<string, Action<KRpcPackage, EndPoint>>()
        {
            { "get_peers", OnGetPeersRequest },
            { "announce_peer", OnAnnouncePeerRequest },
            { "ping", OnPingRequest },
            { "find_node", OnFindNodeRequest }
        }.ToImmutableDictionary();
    }

    public void Start()
    {
        _logger.LogTrace("Starting Node in port {port}", _config.Port);
        Memory<byte> buffer = new byte[MAX_PACK_SIZE];
        _receivedEventArgs.SetBuffer(buffer);
        _receivedEventArgs.Completed += this.OnReceived;
        var endpoint = new IPEndPoint(IPAddress.Any, _config.Port);
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
        foreach (var item in _config.BootstrapList)
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
                        action(request, package, args.RemoteEndPoint!);
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
                        requestProcess(package, args.RemoteEndPoint!);
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

    private void OnGetPeersResponse(
        KRpcPackage request, KRpcPackage response,
        EndPoint remote)
    {
        _logger.LogTrace("received get peers response");
        if (response.Response == null) return;
        if (remote is not IPEndPoint) return;
        var dictionary = response.Response;
        ReadOnlySpan<byte> nodeId = (byte[])dictionary["id"];
        if (_kRouter.TryFoundNode(nodeId, out var node))
        {
            _kRouter.AdjustNode(node);
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
                _logger.LogTrace("received node ID:{id}, IP: {ip}, port: {port} ",
                    BitConverter.ToString(itemId.ToArray()).Replace("-", ""), itemIP, port);
                if (itemId.SequenceEqual(CLIENT_NODE_ID.Span))
                {
                    _logger.LogTrace("found my self, stoped");
                    return;
                }

                if (!_kRouter.TryFoundNode(itemId, out var n))
                {
                    _logger.LogTrace("node not exists in this");
                    // send ping
                    var ping = KademliaProtocols.Ping(CLIENT_NODE_ID.Span);
                    SendPackage(new IPEndPoint(itemIP, port), ping);
                    n = new NodeInfo
                    {
                        NodeId = itemId.ToArray(),
                        Distance = KRouter.ComputeDistances(itemId, CLIENT_NODE_ID.Span),
                        NodeAddress = itemIP,
                        NodePort = port
                    };
                    var prefixLength = _kRouter.AddNode(n);
                    _logger.LogTrace("this node prefix length {l}", prefixLength);
                }

                _logger.LogTrace("add info hash");
                ReadOnlySpan<byte> btih = (byte[])request.Query!.Value.Arguments["info_hash"];
                var torrentInfoHash = _torrentInfoHashManager.AddBitTorrentInfoHash(btih);
                _logger.LogTrace("current node dist {dist}", torrentInfoHash.MaxDistance);
                if (!torrentInfoHash.AnnounceNode(n)) continue;
                _logger.LogTrace("find get peers {btih}", torrentInfoHash.HashText);
                var package = KademliaProtocols.GetPeersRequest(CLIENT_NODE_ID.Span, torrentInfoHash.Hash);
                SendPackage(new IPEndPoint(n.NodeAddress, n.NodePort), package);
            }
        }

        if (!dictionary.TryGetValue("values", out var obj) || obj is not ICollection<object> peers) return;
        {
            _logger.LogTrace("found peers, count {c}", peers.Count);
            ReadOnlySpan<byte> btih = (byte[])request.Query!.Value.Arguments["info_hash"];
            var torrentInfoHash = _torrentInfoHashManager.AddBitTorrentInfoHash(btih);
            _logger.LogTrace("current node dist {dist}", torrentInfoHash.MaxDistance);
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

    private void OnPingResponse(
        KRpcPackage request, KRpcPackage response,
        EndPoint remote)
    {
        _logger.LogTrace("received ping response");
        if (response.Response == null) return;
        if (remote is not IPEndPoint) return;
        var dictionary = response.Response;
        ReadOnlySpan<byte> nodeId = (byte[])dictionary["id"];
        if (_kRouter.TryFoundNode(nodeId, out var node))
        {
            _kRouter.AdjustNode(node);
        }
    }

    private void OnGetPeersRequest(
        KRpcPackage request, EndPoint remote)
    {
        _logger.LogTrace("get peers request");
        if (remote is not IPEndPoint ip) return;
        // save btih
        var query = request.Query!.Value;
        ReadOnlySpan<byte> hash = (byte[])query.Arguments["info_hash"];
        //first try response get peers request
        var list = _kRouter.FindNodeList(hash);
        List<byte> nodes = [];
        foreach (var info in list)
        {
            // nodeid,
            nodes.AddRange(info.NodeId.Span);
            // ip
            nodes.AddRange(info.NodeAddress.GetAddressBytes());
            BigInteger port = info.NodePort;
            // port
            nodes.AddRange(port.ToByteArray(true, true));
        }

        var btih = BitConverter.ToString(hash.ToArray()).Replace("-", "");
        ICollection<byte[]> peers = [];
        if (_torrentInfoHashManager.TryGetBitTorrentInfoHash(btih, out var infoH))
        {
            peers = new List<byte[]>();
            foreach (var peer in infoH.Peers)
            {
                var bytes = peer.Address.GetAddressBytes();
                var port = new BigInteger(peer.Port).ToByteArray(true, true);
                peers.Add([..bytes, ..port]);
            }
        }

        var response = KademliaProtocols.GetPeersResponse(CLIENT_NODE_ID.Span, nodes.ToArray(),
            peers, request.TransactionId);
        SendPackage(ip, response);
        if (peers.Count == 0)
        {
            _logger.LogTrace("begin auto send get peers for {hash}", btih);
            SendGetPeers(hash);
        }

        var semaphore = _fileWrite;
        var magnetLink = $"magnet:?xt=urn:btih:{BitConverter.ToString(hash.ToArray()).Replace("-", "")}";
        _logger.LogTrace("found magnet link {link}", magnetLink);
        try
        {
            semaphore.WaitOne();
            var info = _environment.ContentRootFileProvider.GetFileInfo("bt/magnet.txt");
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

    private void OnAnnouncePeerRequest(
        KRpcPackage request, EndPoint remote)
    {
        _logger.LogTrace("announce peer request");
        if (remote is not IPEndPoint ip) return;
        var arguments = request.Query!.Value.Arguments;
        ReadOnlySpan<byte> id = (byte[])arguments["id"];
        if (_kRouter.TryFoundNode(id, out var node))
        {
            _kRouter.AdjustNode(node);
        }

        ReadOnlySpan<byte> hash = (byte[])arguments["info_hash"];
        var infoHash = _torrentInfoHashManager.AddBitTorrentInfoHash(hash);
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

        var semaphore = _fileWrite;
        var magnetLink = $"magnet:?xt=urn:btih:{BitConverter.ToString(hash.ToArray()).Replace("-", "")}";
        _logger.LogTrace("found magnet link {link}", magnetLink);
        try
        {
            semaphore.WaitOne();
            var info = _environment.ContentRootFileProvider.GetFileInfo("bt/magnet.txt");
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

        _logger.LogTrace("announce response, transaction {tr}", request.FormattedTransaction);
        SendPackage(ip, KademliaProtocols.PingResponse(CLIENT_NODE_ID.Span, request.TransactionId));
    }

    private void OnFindNodeRequest(
        KRpcPackage request, EndPoint remote)
    {
        _logger.LogTrace("find node request");
        if (remote is not IPEndPoint ip) return;
        var arguments = request.Query!.Value.Arguments;
        ReadOnlySpan<byte> nodeId = (byte[])arguments["id"];
        ReadOnlySpan<byte> target = (byte[])arguments["target"];
        if (_kRouter.TryFoundNode(nodeId, out var node))
        {
            _kRouter.AdjustNode(node);
        }
        else
        {
            _kRouter.AddNode(new NodeInfo
            {
                NodeId = nodeId.ToArray(),
                Distance = KRouter.ComputeDistances(nodeId, CLIENT_NODE_ID.Span),
                NodeAddress = ip.Address,
                NodePort = ip.Port
            });
        }

        // find node request, response distance max top 8
        var list = _kRouter.FindNodeList(target);
        List<byte> nodes = [];
        foreach (var info in list)
        {
            // nodeid,
            nodes.AddRange(info.NodeId.Span);
            // ip
            nodes.AddRange(info.NodeAddress.GetAddressBytes());
            BigInteger port = info.NodePort;
            // port
            nodes.AddRange(port.ToByteArray(true, true));
        }

        SendPackage(ip,
            KademliaProtocols.FindNodeResponse(CLIENT_NODE_ID.Span,
                nodes.ToArray(), request.TransactionId));
    }

    private void OnPingRequest(
        KRpcPackage request, EndPoint remote)
    {
        _logger.LogTrace("ping request");
        if (remote is not IPEndPoint ip) return;
        var arguments = request.Query!.Value.Arguments;
        ReadOnlySpan<byte> id = (byte[])arguments["id"];
        if (_kRouter.TryFoundNode(id, out var node))
        {
            _kRouter.AdjustNode(node);
        }

        _logger.LogTrace("ping response, transaction {tr}", request.FormattedTransaction);
        SendPackage(ip, KademliaProtocols.PingResponse(CLIENT_NODE_ID.Span, request.TransactionId));
    }


    private void OnFindNodeResponse(
        KRpcPackage request, KRpcPackage response,
        EndPoint remote)
    {
        if (response.Response == null) return;
        if (remote is not IPEndPoint ip) return;
        _logger.LogTrace("begin process find_node, transaction: {tr}", request.FormattedTransaction);
        var dictionary = response.Response;
        // found node response
        ReadOnlySpan<byte> id = (byte[])dictionary["id"];
        if (!_kRouter.HasNodeExists(id))
        {
            // first, add k-bucket this first node for
            var distance = KRouter.ComputeDistances(id, CLIENT_NODE_ID.Span);
            _kRouter.AddNode(new NodeInfo
            {
                NodeId = id.ToArray(),
                Distance = distance,
                NodeAddress = ip.Address,
                NodePort = ip.Port
            });
        }

        ReadOnlySpan<byte> nodes = (byte[])dictionary["nodes"];
        var nodeCount = nodes.Length / 26;
        _logger.LogTrace("found {count} nodes", nodeCount);
        for (var i = 0; i < nodes.Length; i += 26)
        {
            var item = nodes[i..(i + 26)];
            // node id is
            var itemId = item[..20];
            var itemIP = new IPAddress(item[20..24]);
            var port = (int)new BigInteger(item[24..26], true, true);
            _logger.LogTrace("received node ID:{id}, IP: {ip}, port: {port} ",
                BitConverter.ToString(itemId.ToArray()).Replace("-", ""), itemIP, port);
            if (itemId.SequenceEqual(CLIENT_NODE_ID.Span))
            {
                _logger.LogTrace("found my self, stoped");
                return;
            }

            if (_kRouter.HasNodeExists(itemId))
            {
                _logger.LogTrace("node has already in this");
                continue;
            }

            var node = new NodeInfo
            {
                NodeId = itemId.ToArray(),
                Distance = KRouter.ComputeDistances(itemId, CLIENT_NODE_ID.Span),
                NodeAddress = itemIP,
                NodePort = port
            };
            var prefixLength = _kRouter.AddNode(node);
            _logger.LogInformation("now node count: {c}", _kRouter.NodeCount);
            if (_kRouter.NodeCount <= 15000
                && node.Distance > BigInteger.Zero)
            {
                SendPackage(new IPEndPoint(itemIP, port),
                    KademliaProtocols.FindNode(CLIENT_NODE_ID.Span, CLIENT_NODE_ID.Span));
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
        var distances = KRouter.ComputeDistances(infoHash, CLIENT_NODE_ID.Span);
        var bucket = _kRouter.GetNestDistanceBucket(KRouter.PrefixLength(distances));
        // 找到top8
        var infos = bucket.Take(8);
        foreach (var info in infos)
        {
            var request = KademliaProtocols.GetPeersRequest(CLIENT_NODE_ID.Span, infoHash);
            SendPackage(new IPEndPoint(info.NodeAddress, info.NodePort), request);
        }
    }


    public string GetNodeCount()
    {
        return _kRouter.NodeCount.ToString();
    }

    public string GetBucketCount()
    {
        return _kRouter.KBucketsCount.ToString();
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