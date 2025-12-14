using System.Buffers;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Bittorrent;
using Umi.Dht.Client.Bittorrent.MsgPack;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO;
using Umi.Dht.Client.TorrentIO.StorageInfo;

internal class BitTorrentInfoHashPrivateTracker : IBitTorrentInfoHash
{
    private volatile bool _disposed = false;

    private readonly ReadOnlyMemory<byte> _btih;

    private readonly ITorrentStorage? _storage;

    private readonly Lock _syncPeer = new();

    private readonly LinkedList<IBittorrentPeer> _bittorrentPeers = [];

    private readonly LinkedList<IBittorrentPeer> _activePeers = [];

    private readonly LinkedList<IBittorrentPeer> _deathPeer = [];

    private readonly IServiceScope _scope;

    private readonly IBittorrentPeerFactory _peerFactory;

    private readonly ILogger<BitTorrentInfoHashPrivateTracker> _logger;

    private volatile bool _hasMetadataReceived = false;

    private TorrentFileInfo? _info;

    private readonly LinkedList<(long Piece, ReadOnlyMemory<byte> Buffer)> _metadataBuffers = [];

    private long _pieceSize = 0;

    private long _pieceCount = 0;

    public string HashText => Convert.ToHexString(_btih.Span);
    public ReadOnlySpan<byte> Hash => _btih.Span;
    public BigInteger MaxDistance { get; private set; } = 0;

    private readonly Semaphore _semaphore = new(1, 1);

    public BitTorrentInfoHashPrivateTracker(byte[] btih,
        ILogger<BitTorrentInfoHashPrivateTracker> logger,
        IServiceScope scope)
    {
        _btih = btih;
        _scope = scope;
        _logger = logger;
        _storage = scope.ServiceProvider.GetService<ITorrentStorage>();
        _peerFactory = scope.ServiceProvider.GetRequiredService<IBittorrentPeerFactory>();
        _info = _storage?.Exists(btih);
        _hasMetadataReceived = _info is not null;
    }

    public IReadOnlyList<IPeer> Peers
    {
        get
        {
            ThrowIfDisposed();
            lock (_syncPeer)
            {
                return [.._bittorrentPeers, .._activePeers, .._deathPeer];
            }
        }
    }

    public bool AnnounceNode(NodeInfo node)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        try
        {
            lock (_syncPeer)
            {
                foreach (var peer in peers)
                {
                    if (_bittorrentPeers.Any(p => p.Equals(peer))
                        || _activePeers.Any(p => p.Equals(peer))
                        || _deathPeer.Any(p => p.Equals(peer))) continue;
                    var bittorrentPeer = _peerFactory.CreatePeer(peer, _btih);
                    bittorrentPeer.PeerClose += this.OnSamplePeerOnPeerClose;
                    bittorrentPeer.ExtensionHandshake += this.OnSamplePeerOnExtensionHandshake;
                    bittorrentPeer.PeerExchange += this.OnSamplePeerOnPeerExchange;
                    bittorrentPeer.MetadataPiece += this.SamplePeerOnMetadataPiece;
                    _bittorrentPeers.AddLast(bittorrentPeer);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "add peer error");
        }

        if (_hasMetadataReceived) return;
        this.TryBeginConnect()
            .ConfigureAwait(false)
            .GetAwaiter()
            .OnCompleted(() => _logger.LogTrace("being started some peer"));
    }

    private async Task TryBeginConnect()
    {
        if (_hasMetadataReceived) return;
        var p = new IBittorrentPeer?[5];
        lock (_syncPeer)
        {
            if (_bittorrentPeers.Count > 0 && _activePeers.Count < 5)
            {
                var v = 0;
                var node = _bittorrentPeers.First;
                while (node is not null && v < 5 - _activePeers.Count)
                {
                    p[v] = node.Value;
                    v++;
                    var current = node;
                    node = node.Next;
                    _bittorrentPeers.Remove(current);
                    _activePeers.AddLast(current);
                }
            }
        }

        if (p.All(v => v is null)) return;
        var enumerable = p.Where(v => v is not null)
            .Select(v => v!.Connect())
            .ToArray();
        try
        {
            await Task.WhenAll(enumerable);
        }
        catch (AggregateException e)
        {
            _logger.LogError(e, "task error");
            for (var i = 0; i < enumerable.Length; i++)
            {
                if (enumerable[i].Status != TaskStatus.Faulted) continue;
                lock (_syncPeer)
                {
                    var node = _activePeers.Find(p[i]!);
                    if (node is null) continue;
                    _activePeers.Remove(node);
                    _deathPeer.AddLast(node);
                }
            }

            var beginConnect = this.TryBeginConnect();
            beginConnect.ConfigureAwait(false)
                .GetAwaiter()
                .OnCompleted(() => _logger.LogTrace("retry fill peer until nothing"));
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

        var sequence =
            MetadataSequence.CreateSequenceFromList(_metadataBuffers.Select(p => p.Buffer));
        var buffer = new byte[sequence.Length];
        Memory<byte> merged = buffer;
        sequence.CopyTo(merged.Span);
        using var sha1 = SHA1.Create();
        ReadOnlySpan<byte> computeHash = sha1.ComputeHash(buffer);
        if (!computeHash.SequenceEqual(_btih.Span))
        {
            _logger.LogWarning("info hash compute hash not matched btih:{btih}, sha1: {sha1}",
                Convert.ToHexString(_btih.Span), Convert.ToHexString(computeHash));
            // 丢弃全部数据，重新获取
            _metadataBuffers.Clear();
            lock (_syncPeer)
            {
                var node = _activePeers.First;
                while (node is not null)
                {
                    var current = node;
                    node = node.Next;
                    _activePeers.Remove(current);
                    _deathPeer.AddLast(current);
                    current.Value.Disconnect()
                        .ContinueWith(t => _logger.LogInformation("{s} finished, close connect", t.Status));
                }
            }

            this.TryBeginConnect()
                .ContinueWith(t => _logger.LogTrace("task finished for begin connect"));
            return;
        }

        _info = _storage?.Save(merged);

        _hasMetadataReceived = true;
        _ = this.CloseAllPeerConnection()
            .ContinueWith(t => _logger.LogInformation("{s} finished", t.Status));
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("info dic is {dic}", _info);
        }
    }

    private async Task CloseAllPeerConnection()
    {
        var tasks = new List<Task>();
        lock (_syncPeer)
        {
            if (_activePeers.Count > 0)
            {
                var node = _activePeers.First;
                while (node is not null)
                {
                    try
                    {
                        tasks.Add(node.Value.Disconnect());
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "close error");
                        continue;
                    }

                    node = node.Next;
                }
            }
        }

        await Task.WhenAll(tasks);
    }


    private void OnSamplePeerOnPeerExchange(IBittorrentPeer sender, PeerExchangeEventArg e)
    {
        _logger.LogInformation("peer exchange received");
        _semaphore.WaitOne();
        this.AddPeers(e.Add);
        try
        {
            var remove = e.Remove;
            foreach (var peer in remove)
            {
                lock (_syncPeer)
                {
                    var r = _bittorrentPeers.FirstOrDefault(p => p.Equals(peer));
                    if (r is null) continue;
                    _bittorrentPeers.Remove(r);
                    _deathPeer.AddLast(r);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "remove peer error");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void OnSamplePeerOnExtensionHandshake(IBittorrentPeer peer, ExtensionHandshake e)
    {
        try
        {
            _semaphore.WaitOne();
            _logger.LogInformation("extension handshake finish, peer has metadata exchange: {e} peer exchange {p}, ",
                e.HasMetadataAttr, e.HasPeerExchange);
            this._pieceCount = 0;
            this._pieceSize = e.MetadataLength;
            if (e.HasMetadataAttr)
            {
                // 这里先请求第一片
                peer.GetHashMetadata(0)
                    .GetAwaiter()
                    .OnCompleted(() => _logger.LogDebug("this first piece finished request"));
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void OnSamplePeerOnPeerClose(IBittorrentPeer sender, PeerCloseEventArg e)
    {
        _logger.LogDebug("peer: {address}:{port} closed", sender.Address, sender.Port);
        lock (_syncPeer)
        {
            // find 
            var node = _activePeers.Find(sender);
            if (node is null) return;
            _activePeers.Remove(node);
            _deathPeer.AddLast(node);
        }
    }

    public long MetadataPieceCount => !_hasMetadataReceived
        ? throw new InvalidOperationException("metadata has not been handshaked")
        : _pieceCount;

    public long PieceSize => !_hasMetadataReceived
        ? throw new InvalidOperationException("metadata has not been handshaked")
        : _pieceSize;

    public bool HasMetadataReceived => _hasMetadataReceived;

    public ValueTask BeginGetMetadata()
    {
        ThrowIfDisposed();
        // 废弃方法
        return ValueTask.CompletedTask;
    }

    public TorrentDirectoryInfo TorrentDirectoryInfo => _hasMetadataReceived
        ? _info?.Info ?? throw new InvalidOperationException("info has not been received")
        : throw new InvalidOperationException("metadata has not been received");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_syncPeer)
        {
            if (_bittorrentPeers.Count > 0)
            {
                var first = _bittorrentPeers.First;
                while (first is not null)
                {
                    var current = first;
                    current.Value.Dispose();
                    first = first.Next;
                    _bittorrentPeers.Remove(current);
                }
            }

            if (_activePeers.Count > 0)
            {
                var first = _activePeers.First;
                while (first is not null)
                {
                    var current = first;
                    if (current.Value.IsConnected)
                    {
                        current.Value.Disconnect()
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .OnCompleted(current.Value.Dispose);
                    }
                    else
                    {
                        current.Value.Dispose();
                    }

                    first = first.Next;
                }
            }

            if (_deathPeer.Count > 0)
            {
                var first = _deathPeer.First;
                while (first is not null)
                {
                    var current = first;
                    current.Value.Dispose();
                    first = first.Next;
                    _deathPeer.Remove(current);
                }
            }
        }

        _semaphore.Dispose();
        _scope.Dispose();
    }
}