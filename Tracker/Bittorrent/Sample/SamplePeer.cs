using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Bittorrent.MsgPack;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;
using Umi.Dht.Client.TorrentIO.Utils;
using Umi.Dht.Client.UPnP;
using ThreadState = System.Threading.ThreadState;

namespace Umi.Dht.Client.Bittorrent.Sample;

public sealed class SamplePeer : IBittorrentPeer
{
    private const byte UtMetadataId = 0x2;

    private const byte UtPexId = 0x3;

    private readonly ILogger<SamplePeer> _logger;

    private readonly IServiceProvider _provider;

    private readonly IWanIPResolver _wanIpResolver;

    private readonly Lazy<IPAddress?> _wanIp;

    private readonly byte[] _infoHash;

    private readonly ReadOnlyMemory<byte> _peerId;

    private readonly Socket _client;

    private Timer? _keepAliveTimer;

    private readonly SocketAsyncEventArgs _receiveEventArgs;

    private readonly Pipe _pipe;

    private readonly Thread _processThread;

    private readonly Semaphore _semaphore = new(1, 1);

    private DateTimeOffset _aliveReceived = DateTimeOffset.UtcNow;

    private readonly ConcurrentDictionary<long, Func<long, Task>> _retryMap;

    private volatile bool _finished;

    private volatile bool _hasPeerHandshake = false;
    private volatile bool _hasExtensionHandshake = false;
    private volatile bool _hasMetadataHandshake = false;
    private volatile bool _peerHasPex = false;

    // 握手后赋值
    private long _utMetadataId = 0x0;
    private long _utPexId = 0x0;

    // 对端 peer id
    private readonly Memory<byte> _connectedPeerId;

    private long _pieceLength = 0;
    private long _pieceCount = 0;


    public SamplePeer(IServiceProvider provider, IPeer peer,
        byte[] infoHash,
        byte[] peerId)
    {
        _provider = provider;
        _retryMap = new ConcurrentDictionary<long, Func<long, Task>>();
        _logger = provider.GetRequiredService<ILogger<SamplePeer>>();
        _infoHash = infoHash;
        _pipe = new Pipe();
        _wanIpResolver = provider.GetRequiredService<IWanIPResolver>();
        Node = peer.Node;
        Address = peer.Address;
        Port = peer.Port;
        IsConnected = false;
        _wanIp = new Lazy<IPAddress?>(this.GetWanIp);
        _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _receiveEventArgs = new SocketAsyncEventArgs();
        _receiveEventArgs.SetBuffer(new Memory<byte>(new byte[4096]));
        _receiveEventArgs.Completed += this.OnSocketCompleted;
        _connectedPeerId = new byte[20];
        _peerId = peerId;
        _processThread = new Thread(this.PackageProcess)
        {
            Name = $"{Convert.ToHexStringLower(_peerId.Span)}-Process",
            IsBackground = true
        };
    }

    public NodeInfo Node { get; }

    public IPAddress Address { get; }

    public int Port { get; }

    public bool IsConnected { get; private set; }

    private IPAddress? GetWanIp() => string.IsNullOrEmpty(_wanIpResolver.ExternalIPAddress?.Value)
        ? null
        : IPAddress.Parse(_wanIpResolver.ExternalIPAddress.Value);

    public async Task Connect()
    {
        if (IsConnected) return;
        _logger.LogTrace("begin connecting {address}:{port}", Address, Port);
        try
        {
            using CancellationTokenSource ctx = new();
            ctx.CancelAfter(TimeSpan.FromSeconds(30));
            await _client.ConnectAsync(Address, Port, ctx.Token);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "connect failure");
            throw;
        }

        IsConnected = true;
        _finished = false;
        if (!_client.ReceiveAsync(_receiveEventArgs))
        {
            ThreadPool.QueueUserWorkItem(_ => this.OnSocketCompleted(_client, _receiveEventArgs));
        }

        await this.DoPeerHandshake();
    }

    private async Task DoPeerHandshake()
    {
        Span<byte> reserve = stackalloc byte[8];
        reserve[5] = 0x10;
        var handshake = new BittorrentHandshake
        {
            Protocol = BittorrentHandshake.PROTOCOL_TYPE,
            Header = BittorrentHandshake.HEADER,
            Reserve = reserve,
            PeerId = _peerId.Span,
            InfoHash = _infoHash
        };
        var encode = handshake.Encode();
        // 发送 handshake
        var send = await _client.SendAsync(encode.ToArray());
        _logger.LogDebug("send handshake to {address}:{port}, size is {size}", Address, Port, send);
        _processThread.Start();
        _keepAliveTimer = new Timer(
            _ => this.KeepAlive()
                .ContinueWith(t => _logger.LogTrace("timer keepalive finished, {t}", t.Status))
                .ContinueWith(t =>
                {
                    foreach (var kp in _retryMap)
                    {
                        kp.Value(kp.Key);
                    }
                }),
            null, TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(2));
    }

    public async Task Disconnect()
    {
        if (_client.Connected)
        {
            _logger.LogTrace("disconnecting peer");
            await _client.DisconnectAsync(false);
            IsConnected = false;
            _finished = true;
            this.PeerClose?.Invoke(this, new PeerCloseEventArg(0, false));
        }
    }

    public MetadataPiece Metadata => _hasMetadataHandshake
        ? new MetadataPiece
        {
            PieceCount = _pieceCount,
            PieceLength = _pieceLength
        }
        : throw new InvalidOperationException("Handshake is not complete");

    private void OnSocketCompleted(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            _logger.LogError("receive error {error}, close", args.SocketError);
            if (_client.Connected)
            {
                _client.Close();
                this.PeerClose?.Invoke(this, new PeerCloseEventArg(1, false));
            }

            _finished = true;
            IsConnected = false;
            _pipe.Writer.Complete();
            return;
        }

        var writer = _pipe.Writer;
        var memory = writer.GetMemory(args.BytesTransferred);
        args.MemoryBuffer[..args.BytesTransferred].CopyTo(memory);
        writer.Advance(args.BytesTransferred);
        var resultTask = writer.FlushAsync();
        resultTask.GetAwaiter().OnCompleted(() => _logger.LogTrace("flash completed"));

        // next receive
        if (_client.ReceiveAsync(_receiveEventArgs)) return;
        ThreadPool.QueueUserWorkItem(_ => this.OnSocketCompleted(_client, _receiveEventArgs));
    }

    private void PackageProcess()
    {
        var reader = _pipe.Reader;
        while (!_finished)
        {
            _semaphore.WaitOne();
            try
            {
                if (!_hasPeerHandshake)
                {
                    this.OnPeerHandshake(reader)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                    continue;
                }

                this.OnOtherMessage(reader)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "package process error");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        reader.Complete();
    }


    private async Task OnPeerHandshake(PipeReader reader)
    {
        var result = await reader.ReadAtLeastAsync(BittorrentHandshake.PACKAGE_SIZE)
            .AsTask().ConfigureAwait(false);

        var buffer = result.Buffer;
        if (buffer.IsEmpty)
        {
            _logger.LogTrace("no data read in handshake");
            return;
        }

        var firstByte = buffer.First.Span[0];
        if (firstByte != BittorrentHandshake.PROTOCOL_TYPE)
        {
            _finished = true;
            await _client.DisconnectAsync(false);
            IsConnected = false;
            reader.AdvanceTo(result.Buffer.End);
            await reader.CompleteAsync();
            this.PeerClose?.Invoke(this, new PeerCloseEventArg(2, true));
            throw new FormatException("handshake format error");
        }

        // 字节小了
        if (buffer.Length < 68)
        {
            _finished = true;
            await _client.DisconnectAsync(false);
            IsConnected = false;
            reader.AdvanceTo(result.Buffer.End);
            await reader.CompleteAsync();
            this.PeerClose?.Invoke(this, new PeerCloseEventArg(3, true));
            throw new FormatException("handshake format error");
        }

        // 读取 68 bytes
        Span<byte> span = stackalloc byte[BittorrentHandshake.PACKAGE_SIZE];
        result.Buffer.Slice(result.Buffer.Start, BittorrentHandshake.PACKAGE_SIZE).CopyTo(span);
        var header = span[1..20];
        if (!BittorrentHandshake.HEADER.Equals(Encoding.ASCII.GetString(header)))
        {
            _finished = true;
            await _client.DisconnectAsync(false);
            IsConnected = false;
            reader.AdvanceTo(result.Buffer.End);
            await reader.CompleteAsync();
            this.PeerClose?.Invoke(this, new PeerCloseEventArg(4, true));
            throw new FormatException("handshake format error");
        }

        var reserve = span[20..28];
        var extensionMark = reserve[5];
        span[48..].CopyTo(_connectedPeerId.Span);
        if (!span[28..48].SequenceEqual(_infoHash))
        {
            _logger.LogError("peer is not {btih}'s peer, peer is {pbtih}",
                Convert.ToHexString(_infoHash),
                Convert.ToHexString(span[48..]));
            _finished = true;
            await _client.DisconnectAsync(false);
            IsConnected = false;
            reader.AdvanceTo(result.Buffer.End);
            await reader.CompleteAsync();
            this.PeerClose?.Invoke(this, new PeerCloseEventArg(5, true));
            return;
        }

        if ((extensionMark & 0x10) != 0x10)
        {
            _logger.LogError("peer can not has extension mark, disconnecting");
            _finished = true;
            await _client.DisconnectAsync(false);
            IsConnected = false;
            reader.AdvanceTo(result.Buffer.End);
            await reader.CompleteAsync();
            this.PeerClose?.Invoke(this, new PeerCloseEventArg(7, false));
            return;
        }

        _hasPeerHandshake = true;
        reader.AdvanceTo(result.Buffer.GetPosition(BittorrentHandshake.PACKAGE_SIZE));

        await this.DoExtensionHandshake();
    }

    private async Task OnExtensionHandshake(ReadOnlyMemory<byte> buffer)
    {
        _logger.LogDebug("begin process Extension Handshake");
        var enumerator = buffer.Span[2..].GetEnumerator();
        if (!enumerator.MoveNext()) return;
        var map = BEncoder.BDecodeToMap(ref enumerator);
        if (!map.TryGetValue("m", out var m) || m is not IDictionary<string, object> mSubMap)
        {
            _logger.LogWarning("handshake format error");
            await this.Disconnect();
            return;
        }

        if (mSubMap.TryGetValue("ut_metadata", out var metadata) && metadata is long utMetadata && utMetadata != 0)
        {
            _logger.LogInformation("found ut_metadata extension, id is {id}", utMetadata);
            _utMetadataId = utMetadata;
            _hasMetadataHandshake = true;
            if (map.TryGetValue("metadata_size", out var metadataSize) && metadataSize is long size)
            {
                _pieceLength = size;
                _pieceCount = size / BittorrentMessage.PIECE_SIZE;
                _logger.LogDebug("metadata handshake processed, piece length {l}, total count {c}",
                    _pieceLength, _pieceCount);
            }
        }
        else
        {
            _logger.LogWarning("peer can not do metadata msg");
        }

        if (mSubMap.TryGetValue("ut_pex", out var pex) && pex is long utPex && utPex != 0)
        {
            _logger.LogInformation("found ut_pex extension, id is {id}", utPex);
            _utPexId = utPex;
            _peerHasPex = true;
        }

        _hasExtensionHandshake = true;
        ExtensionHandshake?.Invoke(this,
            new ExtensionHandshake(_hasMetadataHandshake, _hasPeerHandshake, _pieceLength));
        _logger.LogTrace("end process Extension Handshake, other package {package}", map);
    }

    private Task OnMetadataReceived(ReadOnlyMemory<byte> buffer)
    {
        // metadata received
        _logger.LogDebug("begin process Metadata Received");
        // skip 0,1 byte
        var payload = buffer[2..];
        var enumerator = payload.Span.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            _logger.LogWarning("map type unknown");
            return Task.CompletedTask;
        }

        int consumed = 1;
        var map = BEncoder.BDecodeToMap(ref enumerator, ref consumed);
        if (!map.TryGetValue("msg_type", out var omsgtype) || omsgtype is not long msgType)
        {
            _logger.LogWarning("msg_type unknown");
            return Task.CompletedTask;
        }

        if (!map.TryGetValue("piece", out var opiece) || opiece is not long piece)
        {
            _logger.LogWarning("piece unknown");
            return Task.CompletedTask;
        }

        if (!map.TryGetValue("total_size", out var ottsize) || ottsize is not long ttsize)
        {
            ttsize = 0;
        }

        if (msgType != 1)
        {
            this.MetadataPiece?.Invoke(this,
                new MetadataPieceEventArg(Memory<byte>.Empty, piece, msgType, ttsize));
            return Task.CompletedTask;
        }

        consumed++;
        this.MetadataPiece?.Invoke(this, new MetadataPieceEventArg(
            payload[consumed..], piece, msgType, ttsize));
        return Task.CompletedTask;
    }

    private Task OnPeerExchangeMsg(ReadOnlyMemory<byte> buffer)
    {
        _logger.LogDebug("begin process peer exchange message");
        var enumerator = buffer.Span[2..].GetEnumerator();
        if (!enumerator.MoveNext()) return Task.CompletedTask;
        var map = BEncoder.BDecodeToMap(ref enumerator);
        List<IPeer> added = [];
        List<IPeer> dropped = [];
        if (map.TryGetValue("added", out var addedObj) && addedObj is byte[] addedData)
        {
            ReadOnlySpan<byte> compactAddrData = addedData;
            for (var i = 0; i < compactAddrData.Length / 6; i += 6)
            {
                var oneAddr = compactAddrData[i..(i + 6)];
                var addr = new IPAddress(oneAddr[..4]);
                var port = BinaryPrimitives.ReadUInt16BigEndian(oneAddr[4..]);
                var peer = BitTorrentInfoHashManager.CreatePeer(addr, port, Node);
                added.Add(peer);
            }
        }

        if (map.TryGetValue("added.f", out var addrf) && addrf is byte[] addrfData)
        {
            ReadOnlySpan<byte> compactAddrFData = addrfData;
            for (var i = 0; i < compactAddrFData.Length; i++)
            {
                var flag = compactAddrFData[i];
                _logger.LogTrace("this {i} one has {v}", i, flag);
            }
        }

        // dropped
        if (map.TryGetValue("dropped", out var droppedObj) && droppedObj is byte[] droppedData)
        {
            ReadOnlySpan<byte> compactAddrDropped = droppedData;
            for (var i = 0; i < compactAddrDropped.Length / 6; i += 6)
            {
                var oneAddr = compactAddrDropped[i..(i + 6)];
                var addr = new IPAddress(oneAddr[..4]);
                var port = BinaryPrimitives.ReadUInt16BigEndian(oneAddr[4..]);
                var peer = BitTorrentInfoHashManager.CreatePeer(addr, port, Node);
                dropped.Add(peer);
            }
        }

        // invoke event
        this.PeerExchange?.Invoke(this, new PeerExchangeEventArg(added, dropped));
        return Task.CompletedTask;
    }

    private async Task OnOtherMessage(PipeReader reader)
    {
        var header = await reader.ReadAsync().AsTask().ConfigureAwait(false);
        var lengthByte = header.Buffer.FirstSpan[..4];
        var length = BinaryPrimitives.ReadUInt32BigEndian(lengthByte);
        reader.AdvanceTo(header.Buffer.GetPosition(4));
        _logger.LogDebug("received length {l}", length);

        if (length == 0)
        {
            _logger.LogDebug("received an keepalive msg pack");
            _aliveReceived = DateTimeOffset.UtcNow;
            return;
        }

        if (length > BittorrentMessage.PIECE_SIZE + 100)
        {
            reader.AdvanceTo(header.Buffer.End);
            _logger.LogDebug("peer is {addr}:{port}", Address, Port);
            _logger.LogTrace("received keepalive message or lager length, ignored it");
            return;
        }


        // read type and length
        var message = MemoryPool<byte>.Shared.Rent((int)length);
        try
        {
            var buffer = await reader.ReadAsync().AsTask().ConfigureAwait(false);

            while (buffer is { IsCanceled: false, IsCompleted: false } && buffer.Buffer.Length < length)
            {
                _logger.LogTrace("data not full need {nl}, now {l}", length, buffer.Buffer.Length);
                reader.AdvanceTo(buffer.Buffer.Start, buffer.Buffer.End);
                buffer = await reader.ReadAsync().AsTask().ConfigureAwait(false);
            }

            buffer.Buffer.Slice(buffer.Buffer.Start, length)
                .CopyTo(message.Memory.Span[..(int)length]);
            reader.AdvanceTo(buffer.Buffer.GetPosition(length));
            _aliveReceived = DateTimeOffset.UtcNow;
            switch (message.Memory.Span[0])
            {
                case BittorrentMessage.EXTENDED:
                    var extendedType = message.Memory.Span[1];
                    switch (extendedType)
                    {
                        case 0x0:
                            await this.OnExtensionHandshake(message.Memory);
                            break;
                        case UtMetadataId:
                            await this.OnMetadataReceived(message.Memory);
                            break;
                        case UtPexId:
                            await this.OnPeerExchangeMsg(message.Memory);
                            break;
                        default:
                            // other package, ignored
                            _logger.LogWarning("unknown extended type {type} ignored", extendedType);
                            break;
                    }

                    break;
                default:
                    _logger.LogWarning("unknown message type {type} ignored", message.Memory.Span[0]);
                    break;
            }
        }
        finally
        {
            message.Dispose();
        }
    }

    private async Task KeepAlive()
    {
        if (!_hasPeerHandshake)
        {
            _logger.LogWarning("peer has no response for handshake");
            await this.Disconnect();
            return;
        }

        if (_aliveReceived + TimeSpan.FromMinutes(10) < DateTimeOffset.UtcNow)
        {
            // not alive peer ,disconnect
            _logger.LogWarning("peer not alive, pex {p}", _peerHasPex);
            await this.Disconnect();
            return;
        }

        Span<byte> data = stackalloc byte[4];
        var success = BinaryPrimitives.TryWriteUInt32BigEndian(data, 0x0);
        Debug.Assert(success, "Convert failure");
        await _client.SendAsync(data.ToArray());
    }

    private async Task DoExtensionHandshake()
    {
        var mSub = new Dictionary<string, object>
        {
            { "ut_metadata", (long)UtMetadataId },
            { "ut_pex", (long)UtPexId }
        };
        var bd = new Dictionary<string, object>
        {
            { "m", mSub },
            { "v", "libKad/1.0" }
        };
        var encode = BEncoder.BEncode(bd);
        using var memory = MemoryPool<byte>.Shared.Rent(6 + encode.Length);
        BinaryPrimitives.WriteUInt32BigEndian(memory.Memory.Span[..4], 2 + (uint)encode.Length);
        memory.Memory.Span[4] = BittorrentMessage.EXTENDED;
        memory.Memory.Span[5] = 0x0;
        encode.CopyTo(memory.Memory.Span[6..]);
        var sended = await _client.SendAsync(memory.Memory[..(6 + encode.Length)]);
        _logger.LogTrace("send bytes {s}", sended);
    }

    public async Task GetHashMetadata(long piece)
    {
        if (!_hasPeerHandshake || !_hasExtensionHandshake)
        {
            _logger.LogWarning("handshake not completed");
            return;
        }

        var request = new Dictionary<string, object>()
        {
            { "msg_type", BittorrentMessage.METADATA_REQUEST },
            { "piece", piece }
        };
        var requestData = BEncoder.BEncode(request);
        uint length = 2 + (uint)requestData.Length;
        Span<byte> d = stackalloc byte[6 + requestData.Length];
        BinaryPrimitives.WriteUInt32BigEndian(d, length);
        d[4] = BittorrentMessage.EXTENDED;
        d[5] = (byte)_utMetadataId;
        requestData.CopyTo(d[6..]);
        await _client.SendAsync(d.ToArray());

        _retryMap.TryAdd(piece, retryPiece => this.RetryGetHashMetadata(DateTimeOffset.UtcNow, retryPiece, 0));
    }

    private async Task RetryGetHashMetadata(DateTimeOffset sendTime, long piece, uint retryTimes)
    {
        if (sendTime + TimeSpan.FromMinutes(4) >= DateTimeOffset.UtcNow)
        {
            // 没有超时
            return;
        }

        if (retryTimes > 5)
        {
            await this.Disconnect();
            return;
        }

        _retryMap.TryRemove(piece, out _);
        _retryMap.TryAdd(piece,
            realPiece => this.RetryGetHashMetadata(DateTimeOffset.UtcNow, realPiece, retryTimes + 1));
    }

    public Task PeerInterested(bool interested)
    {
        throw new NotImplementedException("Sample Only Request Metadata");
    }

    public Task HavePiece(uint piece)
    {
        throw new NotImplementedException("Sample Only Request Metadata");
    }

    public Task BitField(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException("Sample Only Request Metadata");
    }

    public Task Choke(bool choke)
    {
        throw new NotImplementedException("Sample Only Request Metadata");
    }

    public Task Request(RequestPiece piece)
    {
        throw new NotImplementedException("Sample Only Request Metadata");
    }

    public Task Cancel(RequestPiece piece)
    {
        throw new NotImplementedException("Sample Only Request Metadata");
    }

    public event ExtensionHandshakeEventHandler? ExtensionHandshake;
    public event PeerExchangeEventHandler? PeerExchange;
    public event PeerCloseEventHandler? PeerClose;
    public event MetadataPieceEventHandler? MetadataPiece;
    public event PeerPieceDataEventHandler? PeerPieceData;
    public event PeerBitFieldEventHandler? PeerBitField;
    public event PeerHavePieceEventHandler? PeerHavePiece;

    public bool Equals(IBittorrentPeer? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.Address.Equals(Address) && other.Port == Port;
    }

    public void Dispose()
    {
        try
        {
            _receiveEventArgs.Completed -= this.OnSocketCompleted;
            _receiveEventArgs.Dispose();
            if (_client.Connected)
            {
                _client.Shutdown(SocketShutdown.Both);
                _client.Close();
            }

            _retryMap.Clear();
            _semaphore.Dispose();
            _keepAliveTimer?.Dispose();
            _client.Dispose();
            if (_processThread.ThreadState != ThreadState.Unstarted)
                _processThread.Interrupt();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "when dispose peer error");
        }
    }

    public bool Equals(IPeer? other)
    {
        if (other is null) return false;
        return other.Address.Equals(Address) && other.Port == Port;
    }
}