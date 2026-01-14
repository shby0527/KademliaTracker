using Umi.Dht.Client.Bittorrent;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Protocol;

public interface IDownloaderFactory
{
    IHashInfoDownloader CreateDownloader(in TorrentFileInfo info,
        ReadOnlyMemory<byte> btih,
        IEnumerable<IPeer> peers);
}