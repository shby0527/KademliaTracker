using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Bittorrent;
using Umi.Dht.Client.Bittorrent.Sample;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO;
using Umi.Dht.Client.TorrentIO.StorageInfo;
using Umi.Dht.Client.TorrentIO.Utils;

internal class BitTorrentInfoHashPrivateTracker : IBitTorrentInfoHash
{
    private static readonly RandomNumberGenerator Generator = RandomNumberGenerator.Create();

    private readonly ReadOnlyMemory<byte> _btih;

    private readonly ReadOnlyMemory<byte> _peerId;

    private readonly ITorrentStorage? _storage;

    private readonly LinkedList<IBittorrentPeer> _bittorrentPeers = [];

    private readonly IServiceProvider _provider;

    private readonly ILogger<BitTorrentInfoHashPrivateTracker> _logger;

    private bool _hasMetadataReceived = false;

    private TorrentDirectoryInfo _directoryInfo;

    private readonly LinkedList<(long Piece, ReadOnlyMemory<byte> Buffer)> _metadataBuffers = [];

    private long _pieceSize = 0;

    private long _pieceCount = 0;

    public string HashText => Convert.ToHexString(_btih.Span);
    public ReadOnlySpan<byte> Hash => _btih.Span;
    public BigInteger MaxDistance { get; private set; } = 0;

    public IReadOnlyList<IPeer> Peers => _bittorrentPeers.ToImmutableList();

    private readonly Semaphore _semaphore = new(1, 1);

    public BitTorrentInfoHashPrivateTracker(byte[] btih,
        ILogger<BitTorrentInfoHashPrivateTracker> logger,
        IServiceProvider provider)
    {
        _btih = btih;
        _provider = provider;
        _logger = logger;
        Span<byte> peerId = stackalloc byte[20];
        Generator.GetBytes(peerId);
        _peerId = peerId.ToArray();
        _storage = provider.GetService<ITorrentStorage>();
    }

    public bool AnnounceNode(NodeInfo node)
    {
        try
        {
            _semaphore.WaitOne();
            var distances = KRouter.ComputeDistances(node.NodeId.Span, _btih.Span);
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
                var samplePeer = new SamplePeer(_provider, peer,
                    _provider.GetRequiredService<ILogger<SamplePeer>>(), _btih.ToArray(), _peerId.ToArray());
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
            _logger.LogInformation("metadata received, piece: {piece}", e.Piece);
            if (e.MsgType != 1) return;
            _pieceSize = e.Length;
            // 已经存在的分片，就应该无视
            if (_metadataBuffers.Any(p => p.Piece == e.Piece)) return;
            var buffer = e.Buffer;
            Memory<byte> memory = new byte[buffer.Length];

            buffer.CopyTo(memory);
            memory = memory[..(int)(e.Length > buffer.Length ? buffer.Length : e.Length)];
            var node = _metadataBuffers.Last;
            while (node is not null)
            {
                if (node.Value.Piece < e.Piece) break;
                node = node.Previous;
            }

            if (node is null) _metadataBuffers.AddFirst((e.Piece, memory));
            else _metadataBuffers.AddAfter(node, (e.Piece, memory));

            if (_metadataBuffers.Sum(p => p.Buffer.Length) >= e.Length)
            {
                // 完整的
                this.DoMetadataParser();
                return;
            }

            sender.GetHashMetadata(e.Piece + 1)
                .GetAwaiter().OnCompleted(() => _logger.LogTrace("{picec} received", e.Piece + 1));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void DoMetadataParser()
    {
        _logger.LogInformation("begin parser metadata");

        using var memoryOwner = MemoryPool<byte>.Shared.Rent((int)_pieceSize);
        var memory = memoryOwner.Memory;
        var node = _metadataBuffers.First;
        while (node is not null)
        {
            node.Value.Buffer.CopyTo(memory);
            memory = memory[node.Value.Buffer.Length..];
            node = node.Next;
        }
        //
        // using var sha1 = SHA1.Create();
        // ReadOnlySpan<byte> computeHash = sha1.ComputeHash(memoryOwner.Memory.ToArray());
        // if (!computeHash.SequenceEqual(_btih.Span))
        // {
        //     logger.LogWarning("info hash compute hash not matched btih:{btih}, sha1: {sha1}",
        //         Convert.ToHexString(_btih.Span), Convert.ToHexString(computeHash));
        //     return;
        // }

        _directoryInfo = TorrentFileDecode.DecodeInfo(memoryOwner.Memory.Span);
        _storage?.Save(memoryOwner.Memory.Span);
        _hasMetadataReceived = true;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("info dic is {dic}", _directoryInfo);
        }
    }

    private void OnSamplePeerOnPeerExchange(IBittorrentPeer sender, PeerExchangeEventArg e)
    {
        try
        {
            _semaphore.WaitOne();
            _logger.LogInformation("peer exchange received");
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
            _logger.LogInformation("metadata piece received, length: {l}, count: {c}",
                e.Metadata.PieceLength, e.Metadata.PieceCount);
            this._pieceCount = e.Metadata.PieceCount;
            this._pieceSize = e.Metadata.PieceLength;
            // 这里先请求第一片
            peer.GetHashMetadata(0)
                .GetAwaiter()
                .OnCompleted(() => _logger.LogDebug("this first piece finished request"));
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
            _logger.LogDebug("peer: {address}:{port} closed", sender.Address, sender.Port);
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
            var bittorrentPeer = node.Value;
            lock (_bittorrentPeers)
            {
                var current = node;
                _bittorrentPeers.Remove(current);
                _logger.LogWarning("{btih} peer: {addr}:{port} connected failure, trying next",
                    btih, current.Value.Address, current.Value.Port);
            }

            bittorrentPeer.Dispose();
            node = node.Next;
        }

        _logger.LogWarning("{btih} has no more peers", btih);
    }

    public TorrentDirectoryInfo TorrentDirectoryInfo
    {
        get
        {
            if (_hasMetadataReceived)
            {
                return _directoryInfo;
            }

            throw new InvalidOperationException("metadata has not been received");
        }
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
                    _logger.LogError(e, "Dispose Error, {ip}:{port}", first.Value.Address, first.Value.Port);
                }

                var current = first;
                first = first.Next;
                _bittorrentPeers.Remove(current);
            }
        }
    }
}