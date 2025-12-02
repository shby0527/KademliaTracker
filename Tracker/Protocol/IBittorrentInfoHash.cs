using System.Numerics;
using Umi.Dht.Client.Bittorrent;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.TorrentIO.StorageInfo;

public interface IBitTorrentInfoHash : IDisposable
{
    bool HasMetadataReceived { get; }

    string HashText { get; }

    ReadOnlySpan<byte> Hash { get; }

    BigInteger MaxDistance { get; }

    IReadOnlyList<IPeer> Peers { get; }

    bool AnnounceNode(NodeInfo node);

    void AddPeers(IEnumerable<IPeer> peers);

    public long MetadataPieceCount { get; }

    public long PieceSize { get; }

    ValueTask BeginGetMetadata();

    TorrentDirectoryInfo TorrentDirectoryInfo { get; }
}