using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeer : IPeer, IDisposable
{
    bool IsConnected { get; }

    Task Connect();

    Task Disconnect();

    Task<MetadataPiece> MetadataHandshake();

    Task<IEnumerable<IPeer>> PeersExchange();

    ValueTask<ReadOnlyMemory<byte>> GetHashMetadata(long piece);
}