using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Protocol;

public class BitTorrentInfoHashManager(IServiceProvider provider) : IEnumerable<IBitTorrentInfoHash>
{
    private readonly ILogger<BitTorrentInfoHashManager> _logger =
        provider.GetRequiredService<ILogger<BitTorrentInfoHashManager>>();

    private readonly Dictionary<string, IBitTorrentInfoHash> _bitTorrentInfo = new();

    public IBitTorrentInfoHash this[string btih] => _bitTorrentInfo[btih];

    public int Count => _bitTorrentInfo.Count;

    public IBitTorrentInfoHash AddBitTorrentInfoHash(ReadOnlySpan<byte> infoHash)
    {
        var btih = BitConverter.ToString(infoHash.ToArray()).Replace("-", "");
        lock (_bitTorrentInfo)
        {
            if (_bitTorrentInfo.TryGetValue(btih, out var hash)) return hash;
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

    public void TryReceiveInfoHashMetadata()
    {
        // 尝试开始获取info hash 的 metadata， 可能的
        foreach (var hash in _bitTorrentInfo)
        {
            if (hash.Value.HasMetadataReceived) continue;
            hash.Value.BeginGetMetadata();
        }
    }


    private class BitTorrentInfoHashPrivateTracker(byte[] btih) : IBitTorrentInfoHash
    {
        private readonly ReadOnlyMemory<byte> _btih = btih;

        private readonly List<IPeer> _peers = [];

        private bool _hasMetadataReceived = false;

        private long _pieceSize = 0;

        private long _pieceCount = 0;

        public string HashText => BitConverter.ToString(btih).Replace("-", "");
        public ReadOnlySpan<byte> Hash => _btih.Span;
        public BigInteger MaxDistance { get; private set; } = 0;

        public IReadOnlyList<IPeer> Peers => _peers.ToImmutableList();

        private readonly Semaphore _semaphore = new(1, 1);

        public bool AnnounceNode(NodeInfo node)
        {
            try
            {
                _semaphore.WaitOne();
                var distances = KRouter.ComputeDistances(node.NodeId.Span, btih);
                if (MaxDistance > distances) return false;
                MaxDistance = distances;
                return true;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void AddPeers(IEnumerable<IPeer> peers)
        {
            foreach (var peer in peers)
            {
                if (!_peers.Exists(e => e.Equals(peer)))
                {
                    _peers.Add(peer);
                }
            }
        }

        public long MetadataPieceCount
        {
            get
            {
                if (!_hasMetadataReceived)
                {
                    throw new InvalidOperationException("metadata has not been received");
                }

                return _pieceCount;
            }
        }

        public long PieceSize
        {
            get
            {
                if (!_hasMetadataReceived)
                {
                    throw new InvalidOperationException("metadata has not been received");
                }

                return _pieceSize;
            }
        }

        public bool HasMetadataReceived => _hasMetadataReceived;

        public void BeginGetMetadata()
        {
            if (_peers.Count == 0)
            {
                return;
            }

            // 这里开始获取相关属性
            //
            _hasMetadataReceived = true;
        }
    }

    private class PeerInfoPrivateTracker(IPAddress address, int port, NodeInfo node) : IPeer
    {
        public NodeInfo Node => node;
        public IPAddress Address => address;
        public int Port => port;

        public bool Equals(IPeer? other)
        {
            if (other is null) return false;
            return this.Address.Equals(other.Address) && this.Port == other.Port;
        }
    }

    public IEnumerator<IBitTorrentInfoHash> GetEnumerator()
    {
        return _bitTorrentInfo.Select(hash => hash.Value).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }
}

public interface IBitTorrentInfoHash
{
    bool HasMetadataReceived { get; }

    string HashText { get; }

    ReadOnlySpan<byte> Hash { get; }

    BigInteger MaxDistance { get; }

    IReadOnlyList<IPeer> Peers { get; }

    bool AnnounceNode(NodeInfo node);

    void AddPeers(IEnumerable<IPeer> peers);

    public long MetadataPieceCount { get; }

    public long PieceSize { get; }

    void BeginGetMetadata();
}

public interface IPeer : IEquatable<IPeer>
{
    NodeInfo Node { get; }

    IPAddress Address { get; }

    int Port { get; }
}