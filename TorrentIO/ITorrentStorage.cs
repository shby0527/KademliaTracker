using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.TorrentIO;

public interface ITorrentStorage
{
    TorrentFileInfo Save(ReadOnlySpan<byte> data);

    IEnumerable<TorrentFileInfo> Search(string file);

    TorrentFileInfo? Exists(ReadOnlySpan<byte> infohash);
}