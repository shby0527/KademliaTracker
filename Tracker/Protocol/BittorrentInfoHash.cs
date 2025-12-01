using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
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

    private readonly LinkedList<ReadOnlyMemory<byte>> _metadataBuffers = [];

    private readonly BigInteger _pieceBitMap = BigInteger.Zero;

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
                var samplePeer = new SamplePeer(provider, peer,
                    provider.GetRequiredService<ILogger<SamplePeer>>(), _btih.ToArray());
                samplePeer.PeerClose += OnSamplePeerOnPeerClose;
                samplePeer.MetadataHandshake += OnSamplePeerOnMetadataHandshake;
                samplePeer.PeerExchange += OnSamplePeerOnPeerExchange;
                samplePeer.MetadataPiece += SamplePeerOnMetadataPiece;
                _bittorrentPeers.AddLast(samplePeer);
            }
        }
    }

    private void SamplePeerOnMetadataPiece(IBittorrentPeer sender, MetadataPieceEventArg e)
    {
        try
        {
            _semaphore.WaitOne();
            logger.LogInformation("metadata received");
            if (e.MsgType == 1)
            {
                var buffer = e.Buffer;
                _metadataBuffers.AddLast(buffer[..(int)e.Length]);
                if (_metadataBuffers.Sum(e => e.Length) > e.Length)
                {
                    _pieceSize = e.Length;
                    // 完整的
                    this.DoMetadataParser();
                    return;
                }

                sender.GetHashMetadata(e.Piece + 1)
                    .GetAwaiter().OnCompleted(() => logger.LogTrace("{picec} received", e.Piece + 1));
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void DoMetadataParser()
    {
        
        _hasMetadataReceived = true;
    }

    private void OnSamplePeerOnPeerExchange(IBittorrentPeer sender, PeerExchangeEventArg e)
    {
        try
        {
            _semaphore.WaitOne();
            logger.LogInformation("peer exchange received");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void OnSamplePeerOnMetadataHandshake(IBittorrentPeer peer, MetadataHandshakeEventArg e)
    {
        try
        {
            _semaphore.WaitOne();
            logger.LogInformation("metadata piece received, length: {l}, count: {c}",
                e.Metadata.PieceLength, e.Metadata.PieceCount);
            this._pieceCount = e.Metadata.PieceCount;
            this._pieceSize = e.Metadata.PieceLength;
            // 这里先请求第一片
            peer.GetHashMetadata(0)
                .GetAwaiter()
                .OnCompleted(() => logger.LogDebug("this first piece finished request"));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void OnSamplePeerOnPeerClose(IPeer sender, PeerCloseEventArg e)
    {
        try
        {
            _semaphore.WaitOne();
            logger.LogDebug("peer: {address}:{port} closed", sender.Address, sender.Port);
            var node = _bittorrentPeers.Find((IBittorrentPeer)sender);
            if (node is not null) _bittorrentPeers.Remove(node);
        }
        finally
        {
            _semaphore.Release();
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

        var btih = Convert.ToHexString(_btih.Span);
        var node = _bittorrentPeers.First;
        while (node is not null)
        {
            await node.Value.Connect();
            if (node.Value.IsConnected) break;
            var current = node;
            node = node.Next;
            current.Value.Dispose();
            _bittorrentPeers.Remove(current);
            logger.LogWarning("{btih} peer: {addr}:{port} connected failure, trying next",
                btih, current.Value.Address, current.Value.Port);
        }

        logger.LogWarning("{btih} has no more peers", btih);
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