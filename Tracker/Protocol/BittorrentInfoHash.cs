using System.Collections.Immutable;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Bittorrent;
using Umi.Dht.Client.Bittorrent.Sample;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO;

internal class BitTorrentInfoHashPrivateTracker(
    byte[] btih,
    ILogger<BitTorrentInfoHashPrivateTracker> logger,
    IServiceProvider provider)
    : IBitTorrentInfoHash
{
    private readonly ReadOnlyMemory<byte> _btih = btih;

    private readonly ITorrentStorage? _storage = provider.GetService<ITorrentStorage>();

    private readonly LinkedList<IBittorrentPeer> _bittorrentPeers = [];

    private bool _hasMetadataReceived = false;

    private long _pieceSize = 0;

    private long _pieceCount = 0;

    public string HashText => BitConverter.ToString(btih).Replace("-", "");
    public ReadOnlySpan<byte> Hash => _btih.Span;
    public BigInteger MaxDistance { get; private set; } = 0;

    public IReadOnlyList<IPeer> Peers => _bittorrentPeers.ToImmutableList();

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
            if (!_bittorrentPeers.Any(p => p.Equals(peer)))
            {
                _bittorrentPeers.AddLast(new SamplePeer(provider, peer,
                    provider.GetRequiredService<ILogger<SamplePeer>>(), _btih.ToArray()));
            }
        }
    }

    public long MetadataPieceCount
    {
        get
        {
            if (!_hasMetadataReceived)
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
            if (!_hasMetadataReceived)
            {
                throw new InvalidOperationException("metadata has not been handshaked");
            }

            return _pieceSize;
        }
    }

    public bool HasMetadataReceived => _hasMetadataReceived;

    public async ValueTask BeginGetMetadata()
    {
        if (_bittorrentPeers.Count == 0)
        {
            return;
        }

        var t = from p in _bittorrentPeers
            where !p.IsConnected
            select p;
        
        foreach (var bittorrentPeer in t.Take(1))
        {
            await bittorrentPeer.Connect();
        }

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