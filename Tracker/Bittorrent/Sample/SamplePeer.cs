using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent.Sample;

public sealed class SamplePeer : IBittorrentPeer
{
    private readonly ILogger<SamplePeer> _logger;

    private readonly IServiceProvider _provider;

    private bool _hasExtensionHandshake = false;
    private bool _hasMetadataHandshake = false;

    private long _pieceLength = 0;
    private long _pieceCount = 0;

    public SamplePeer(IServiceProvider provider, IPeer peer, ILogger<SamplePeer> logger)
    {
        _provider = provider;
        _logger = logger;
        Node = peer.Node;
        Address = peer.Address;
        Port = peer.Port;
        IsConnected = false;
    }

    public NodeInfo Node { get; }

    public IPAddress Address { get; }

    public int Port { get; }

    public bool IsConnected { get; private set; }

    public async Task Connect()
    {
        throw new NotImplementedException();
    }

    public async Task Disconnect()
    {
        throw new NotImplementedException();
    }

    public async Task ExtensionHandshake()
    {
        throw new NotImplementedException();
    }

    public async Task<MetadataPiece> MetadataHandshake()
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<IPeer>> PeersExchange()
    {
        throw new NotImplementedException();
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
    }

    public bool Equals(IPeer? other)
    {
        if (other is null) return false;
        return other.Address.Equals(Address) && other.Port == Port;
    }
}