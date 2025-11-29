using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Bittorrent.MsgPack;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;
using Umi.Dht.Client.UPnP;

namespace Umi.Dht.Client.Bittorrent.Sample;

public sealed class SamplePeer : IBittorrentPeer
{
    private static RandomNumberGenerator _generator = RandomNumberGenerator.Create();

    private readonly ILogger<SamplePeer> _logger;

    private readonly IServiceProvider _provider;

    private readonly IWanIPResolver _wanIpResolver;

    private readonly Lazy<IPAddress?> _wanIp;

    private readonly byte[] _infoHash;

    private readonly ReadOnlyMemory<byte> _peerId;

    private readonly Socket _client;

    private readonly SocketAsyncEventArgs _receiveEventArgs;

    private readonly Pipe _pipe;

    private readonly Thread _processThread;

    private readonly Semaphore _semaphore;

    private volatile bool _finished;

    private volatile bool _hasPeerHandshake = false;
    private volatile bool _hasExtensionHandshake = false;
    private volatile bool _hasMetadataHandshake = false;
    private volatile bool _peerHasPex = false;

    // 对端 peer id
    private Memory<byte> _connectedPeerId;

    private long _pieceLength = 0;
    private long _pieceCount = 0;


    public SamplePeer(IServiceProvider provider, IPeer peer, ILogger<SamplePeer> logger, byte[] infoHash)
    {
        _provider = provider;
        _logger = logger;
        _infoHash = infoHash;
        _pipe = new Pipe();
        _semaphore = new Semaphore(1, 1);
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
        Span<byte> peerId = stackalloc byte[20];
        // 直接随机生成一个 
        _connectedPeerId = new byte[20];
        _generator.GetBytes(peerId);
        _peerId = new ReadOnlyMemory<byte>(peerId.ToArray());
        _processThread = new Thread(this.PackageProcess)
        {
            Name = $"{Convert.ToHexStringLower(_peerId.Span)}-Process",
            IsBackground = true
        };
        _processThread.Start();
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
        _logger.LogTrace("begin connecting {address}:{port}", Address, Port);
        await _client.ConnectAsync(Address, Port);
        IsConnected = true;
        _finished = false;
        if (!_client.ReceiveAsync(_receiveEventArgs))
        {
            ThreadPool.QueueUserWorkItem(_ => this.OnSocketCompleted(_client, _receiveEventArgs));
        }

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
    }

    public async Task Disconnect()
    {
        if (_client.Connected)
        {
            await _client.DisconnectAsync(false);
            IsConnected = false;
            _finished = true;
        }
    }

    private void OnSocketCompleted(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            _logger.LogError("receive error {error}, close", args.SocketError);
            _client.Close();
            _finished = true;
            IsConnected = false;
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

                if (_hasPeerHandshake && !_hasExtensionHandshake)
                {
                    // extensionHandshake
                    this.OnExtensionHandshake(reader)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                    continue;
                }

                if (_hasExtensionHandshake && !_hasMetadataHandshake)
                {
                    this.OnMetadataHandshake(reader)
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
                _logger.LogError(e, "error");
            }
        }

        reader.Complete();
    }


    private async Task OnPeerHandshake(PipeReader reader)
    {
        try
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;
            var firstByte = buffer.First.Span[0];
            if (firstByte != BittorrentHandshake.PROTOCOL_TYPE)
            {
                throw new FormatException("handshake format error");
            }

            _semaphore.WaitOne();
            // 读取 68 bytes
            Span<byte> span = stackalloc byte[68];
            result.Buffer.CopyTo(span);
            var header = span[1..20];
            if (!BittorrentHandshake.HEADER.Equals(Encoding.ASCII.GetString(header)))
            {
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
            }

            if ((extensionMark & 0x10) != 0x10)
            {
                _logger.LogError("peer can not has extension mark, disconnecting");
                _finished = true;
                await _client.DisconnectAsync(false);
                IsConnected = false;
            }

            _hasPeerHandshake = true;
        }
        finally
        {
            _semaphore.Release();
        }

        await this.ExtensionHandshake();
    }

    private async Task OnExtensionHandshake(PipeReader reader)
    {
    }

    private async Task OnMetadataHandshake(PipeReader reader)
    {
    }

    private async Task OnOtherMessage(PipeReader reader)
    {
    }


    public async Task ExtensionHandshake()
    {
        var bd = new Dictionary<string, object>();
        
    }

    public async Task<MetadataPiece> MetadataHandshake()
    {
        return default;
    }

    public async Task<IEnumerable<IPeer>> PeersExchange()
    {
        if (!_peerHasPex) return [];

        return [];
    }

    public async ValueTask<ReadOnlyMemory<byte>> GetHashMetadata(long piece)
    {
        throw new NotImplementedException();
    }


    public bool Equals(IBittorrentPeer? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.Address.Equals(Address) && other.Port == Port;
    }

    public void Dispose()
    {
        _receiveEventArgs.Completed -= this.OnSocketCompleted;
        _receiveEventArgs.Dispose();
        if (_client.Connected)
        {
            _client.Close();
        }

        _semaphore.Dispose();
        _client.Dispose();
        _processThread.Interrupt();
    }

    public bool Equals(IPeer? other)
    {
        if (other is null) return false;
        return other.Address.Equals(Address) && other.Port == Port;
    }
}