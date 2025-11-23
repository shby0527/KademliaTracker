using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent.Sample;

public sealed class SamplePeer : IBittorrentPeer
{
    public NodeInfo Node { get; }
    public IPAddress Address { get; }
    public int Port { get; }
    public bool IsConnected { get; }
    public async Task Connect()
    {
        throw new NotImplementedException();
    }

    public async Task Disconnect()
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


    public bool Equals(IPeer? other)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }
}