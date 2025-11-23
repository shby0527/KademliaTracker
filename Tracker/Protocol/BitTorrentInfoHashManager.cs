using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Bittorrent;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Protocol;

public class BitTorrentInfoHashManager(IServiceProvider provider) : IEnumerable<IBitTorrentInfoHash>, IDisposable
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
            hash = new BitTorrentInfoHashPrivateTracker(infoHash.ToArray(),
                provider.GetRequiredService<ILogger<BitTorrentInfoHashPrivateTracker>>());
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

    public IEnumerator<IBitTorrentInfoHash> GetEnumerator()
    {
        return _bitTorrentInfo.Select(hash => hash.Value).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    public void Dispose()
    {
        foreach (var bitTorrentInfoHash in _bitTorrentInfo)
        {
            bitTorrentInfoHash.Value.Dispose();
        }
    }


    private class BitTorrentInfoHashPrivateTracker(byte[] btih, ILogger<BitTorrentInfoHashPrivateTracker> logger)
        : IBitTorrentInfoHash
    {
        private readonly ReadOnlyMemory<byte> _btih = btih;

        private readonly List<IPeer> _peers = [];

        private readonly LinkedList<IBittorrentPeer> _bittorrentPeers = [];

        private bool _hasMetadataReceived = false;

        private bool _hasHandshake = false;

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
                if (!_hasHandshake)
                {
                    throw new InvalidOperationException("metadata has not been handshaked");
                }

                return _pieceCount;
            }
        }

        public long PieceSize
        {
            get
            {
                if (!_hasHandshake)
                {
                    throw new InvalidOperationException("metadata has not been handshaked");
                }

                return _pieceSize;
            }
        }

        public bool HasMetadataReceived => _hasMetadataReceived;

        public async ValueTask<MetadataPiece> MetadataPieceHandshake()
        {
            if (_hasHandshake)
                return await ValueTask.FromResult(new MetadataPiece
                {
                    PieceLength = _pieceSize,
                    PieceCount = _pieceCount,
                });
            _hasHandshake = true;

            return default;
        }

        public async ValueTask BeginGetMetadata()
        {
            if (_peers.Count == 0)
            {
                return;
            }

            var metadata = await this.MetadataPieceHandshake();
            // 这里开始获取相关属性
            //
            _hasMetadataReceived = true;
        }

        public void Dispose()
        {
            _semaphore.Dispose();
            if (_bittorrentPeers.Count > 0)
            {
                var first = _bittorrentPeers.First;
                while (first is not null)
                {
                    try
                    {
                        first.Value.Disconnect();
                        first.Value.Dispose();
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Dispose Error, {ip}:{port}", first.Value.Address, first.Value.Port);
                    }

                    var current = first;
                    first = first.Next;
                    _bittorrentPeers.Remove(current);
                }
            }
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
}

public interface IBitTorrentInfoHash : IDisposable
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

    ValueTask<MetadataPiece> MetadataPieceHandshake();

    ValueTask BeginGetMetadata();
}

public interface IPeer : IEquatable<IPeer>
{
    NodeInfo Node { get; }

    IPAddress Address { get; }

    int Port { get; }
}