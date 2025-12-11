using System.Text;

namespace Umi.Dht.Client.TorrentIO.StorageInfo;

public readonly struct MagnetInfo
{
    public required ReadOnlyMemory<byte> Hash { get; init; }

    public string? DisplayName { get; init; }

    public long? ExactLength { get; init; }

    public IEnumerable<string>? Trackers { get; init; }

    public override string ToString()
    {
        var s = new StringBuilder($"magnet:?xt=urn:btih:{BitConverter.ToString(Hash.ToArray()).Replace("-", "")}");
        if (!string.IsNullOrEmpty(DisplayName))
        {
            s.AppendJoin('&', $"dn={DisplayName}");
        }

        if (ExactLength is not null)
        {
            s.AppendJoin('&', $"xl={ExactLength}");
        }

        if (Trackers is not null)
        {
            foreach (var tracker in Trackers)
            {
                s.AppendJoin('&', $"tr={tracker}");
            }
        }

        return s.ToString();
    }
}