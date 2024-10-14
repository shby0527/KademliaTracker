using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Bittorrent.Sample;

public sealed class SamplePeer : IBittorrentPeer, IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    private readonly SocketAsyncEventArgs _socketAsyncEventArgs = new();

    private readonly IServiceProvider _serviceProvider;

    private readonly ReadOnlyMemory<byte> PEER_ID;

    private readonly ReadOnlyMemory<byte> INFO_HASH;

    private readonly ILogger<SamplePeer> _logger;

    public SamplePeer(IPeer peer, IServiceProvider provider, ReadOnlySpan<byte> peerId, ReadOnlySpan<byte> infoHash)
    {
        Peer = peer;
        _serviceProvider = provider;
        _logger = provider.GetRequiredService<ILogger<SamplePeer>>();
        PEER_ID = peerId.ToArray();
        INFO_HASH = infoHash.ToArray();
        _socketAsyncEventArgs.Completed += OnReceiveEvent;
        Memory<byte> buffer = new byte[4096];
        _socketAsyncEventArgs.SetBuffer(buffer);
    }

    public bool IsConnected { get; private set; } = false;

    public IPeer Peer { get; }

    public void Connect()
    {
        if (IsConnected)
        {
            throw new InvalidOperationException("Already connected");
        }

        _socket.Connect(Peer.Address, Peer.Port);
        _socket.ReceiveAsync(_socketAsyncEventArgs);
        this.SendHandshake();
    }


    private void SendHandshake()
    {
        // bittorrent handshake package
        BittorrentHandshake handshake = new()
        {
            PeerId = PEER_ID.ToArray(),
            InfoHash = INFO_HASH.ToArray()
        };
        _socket.Send(handshake.Encode());
    }

    public void Disconnect()
    {
        if (IsConnected == false)
        {
            throw new InvalidOperationException("Not connected");
        }
    }

    public ReadOnlySpan<byte> GetHashMetadata()
    {
        if (IsConnected == false)
        {
            throw new InvalidOperationException("Not connected");
        }

        return [];
    }

    private void OnReceiveEvent(object? sender, SocketAsyncEventArgs e)
    {
        if (e is not { BytesTransferred: > 0, SocketError: SocketError.Success })
        {
            _logger.LogTrace("remote closed");
            return;
        }
    }

    public void Dispose()
    {
        _socket.Dispose();
        _socketAsyncEventArgs.Completed -= OnReceiveEvent;
        _socketAsyncEventArgs.Dispose();
    }
}