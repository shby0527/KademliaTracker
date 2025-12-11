using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.TorrentIO;

public interface ITorrentStorage
{
    /// <summary>
    /// save info dictionary 
    /// </summary>
    /// <param name="data">info dictionary</param>
    /// <returns></returns>
    TorrentFileInfo Save(ReadOnlyMemory<byte> data);

    IEnumerable<TorrentFileInfo> Search(string file);

    TorrentFileInfo? Exists(ReadOnlySpan<byte> infohash);
}