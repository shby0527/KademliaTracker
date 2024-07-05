using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Protocol;

public class BitTorrentInfoHashManager(IServiceProvider provider)
{
    private readonly ILogger<BitTorrentInfoHashManager> _logger =
        provider.GetRequiredService<ILogger<BitTorrentInfoHashManager>>();

    private readonly Dictionary<string, IBitTorrentInfoHash> _bitTorrentInfo = new();


    public IBitTorrentInfoHash this[string btih] => _bitTorrentInfo[btih];


    public IBitTorrentInfoHash AddBitTorrentInfoHash(ReadOnlySpan<byte> infoHash)
    {
        var btih = BitConverter.ToString(infoHash.ToArray()).Replace("-", "");
        lock (_bitTorrentInfo)
        {
            IBitTorrentInfoHash hash;
            if (_bitTorrentInfo.TryGetValue(btih, out hash)) return hash;
            hash = new BitTorrentInfoHashPrivateTracker(infoHash.ToArray());
            _bitTorrentInfo[btih] = hash;
            return hash;
        }
    }


    public bool TryGetBitTorrentInfoHash(string btih, [MaybeNullWhen(false)] out IBitTorrentInfoHash bitTorrentInfoHash)
    {
        _logger.LogTrace("try get btih {btih} info", btih);
        return _bitTorrentInfo.TryGetValue(btih, out bitTorrentInfoHash);
    }

    public static IPeer CreatePeer(IPAddress address, int port, NodeInfo node)
    {
        return new PeerInfoPrivateTracker(address, port, node);
    }

    private class BitTorrentInfoHashPrivateTracker(byte[] btih) : IBitTorrentInfoHash
    {
        private readonly ReadOnlyMemory<byte> _btih = btih;

        private readonly List<IPeer> _peers = [];

        public string HashText => BitConverter.ToString(btih).Replace("-", "");
        public ReadOnlySpan<byte> Hash => _btih.Span;
        public int MaxDistance { get; private set; } = 0;

        public IReadOnlyList<IPeer> Peers => _peers.ToImmutableList();

        private readonly Semaphore _semaphore = new(1, 1);

        public bool AnnounceNode(NodeInfo node)
        {
            try
            {
                _semaphore.WaitOne();
                var distances = KBucket.ComputeDistances(node.NodeID.Span, btih);
                var prefixLength = KBucket.PrefixLength(distances);
                if (MaxDistance > prefixLength) return false;
                MaxDistance = prefixLength;
                return true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void AddPeers(IEnumerable<IPeer> peers)
        {
            _peers.AddRange(peers);
        }

        public void BeginGetMetadata()
        {
            
        }
    }

    private class PeerInfoPrivateTracker(IPAddress address, int port, NodeInfo node) : IPeer
    {
        public NodeInfo Node => node;
        public IPAddress Address => address;
        public int Port => port;
    }
}

public interface IBitTorrentInfoHash
{
    string HashText { get; }

    ReadOnlySpan<byte> Hash { get; }

    int MaxDistance { get; }

    IReadOnlyList<IPeer> Peers { get; }

    bool AnnounceNode(NodeInfo node);

    void AddPeers(IEnumerable<IPeer> peers);

    void BeginGetMetadata();
}

public interface IPeer
{
    NodeInfo Node { get; }

    IPAddress Address { get; }

    int Port { get; }
}