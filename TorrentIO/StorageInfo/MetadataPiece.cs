namespace Umi.Dht.Client.TorrentIO.StorageInfo;

public readonly struct MetadataPiece
{
    public required long PieceLength { get; init; }

    public required long PieceCount { get; init; }
}