using System.Text;

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

    public IEnumerable<string>? UrlList { get; init; }

    public override string ToString()
    {
        var sb = new StringBuilder($"Announce: {Announce}\n, Info: {Info.ToString()}");
        foreach (var item in AnnounceList ?? [])
        {
            sb.AppendLine("-");
            foreach (var p in item)
            {
                sb.AppendLine($"\t {p}");
            }
        }

        if (CreationDate != null)
        {
            sb.AppendLine($"CreationDate: {CreationDate}");
        }

        if (CreatedBy != null)
        {
            sb.AppendLine($"CreatedBy: {CreatedBy}");
        }

        if (Comment != null)
        {
            sb.AppendLine($"Comment: {Comment}");
        }

        if (Encoding != null)
        {
            sb.AppendLine($"Encoding: {Encoding}");
        }

        foreach (var item in UrlList ?? [])
        {
            sb.AppendLine($"\t{item}");
        }

        return sb.ToString();
    }
}

public readonly struct TorrentDirectoryInfo
{
    public required string Name { get; init; }

    public required long PieceLength { get; init; }

    public required IEnumerable<byte[]> Pieces { get; init; }

    public long? Length { get; init; }

    public IEnumerable<TorrentFileList>? Files { get; init; }

    public override string ToString()
    {
        var sb = new StringBuilder($"Name: {Name}\n Piece Length: {PieceLength}\n");
        foreach (var piece in Pieces)
        {
            sb.AppendLine($"\t - {BitConverter.ToString(piece).Replace("-", "")}");
        }

        if (Length != null)
        {
            sb.AppendLine($"Length: {Length}");
        }

        foreach (var item in Files ?? [])
        {
            sb.AppendLine($"\t{item.ToString()}");
        }

        return sb.ToString();
    }
}

public readonly struct TorrentFileList
{
    public required long Length { get; init; }

    public required string Path { get; init; }

    public override string ToString()
    {
        return $"Path: {Path} ,Length: {Length}";
    }
}