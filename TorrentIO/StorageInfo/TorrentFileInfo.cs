namespace Umi.Dht.Client.TorrentIO.StorageInfo;

public readonly struct TorrentFileInfo
{
    public required string Announce { get; init; }

    public required TorrentDirectoryInfo Info { get; init; }

    public IEnumerable<IEnumerable<string>>? AnnounceList { get; init; }

    public long? CreationDate { get; init; }

    public string? CreatedBy { get; init; }

    public string? Comment { get; init; }

    public string? Encoding { get; init; }
}

public readonly struct TorrentDirectoryInfo
{
    public required string Name { get; init; }

    public required long PieceLength { get; init; }

    public required IEnumerable<long> Pieces { get; init; }

    public long? Length { get; init; }

    public IEnumerable<TorrentFileList>? Files { get; init; }
}

public readonly struct TorrentFileList
{
    public required long Length { get; init; }

    public required string Path { get; init; }
}