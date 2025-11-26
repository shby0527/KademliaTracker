using System.Net;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeer : IPeer, IEquatable<IBittorrentPeer>, IEquatable<IPeer>, IDisposable
{
    bool IsConnected { get; }

    Task Connect();

    Task Disconnect();

    Task ExtensionHandshake();

    Task<MetadataPiece> MetadataHandshake();

    Task<IEnumerable<IPeer>> PeersExchange();

    ValueTask<ReadOnlyMemory<byte>> GetHashMetadata(long piece);
}

public interface IPeer
{
    NodeInfo Node { get; }

    IPAddress Address { get; }

    int Port { get; }
}