using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.TorrentIO;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Plugins.FileSystemStorage;

[Service(ServiceScope.Singleton)]
public sealed class FileSystemTorrentStorage(
    ILogger<FileSystemTorrentStorage> logger,
    IHostEnvironment environment) : ITorrentStorage
{
    public TorrentFileInfo Save(ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<TorrentFileInfo> Search(string file)
    {
        throw new NotImplementedException();
    }

    public TorrentFileInfo? Exists(ReadOnlySpan<byte> infohash)
    {
        throw new NotImplementedException();
    }
}