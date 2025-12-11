using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.TorrentIO;
using Umi.Dht.Client.TorrentIO.StorageInfo;
using Umi.Dht.Client.TorrentIO.Utils;

namespace Umi.Dht.Client.Plugins.FileSystemStorage;

[Service(ServiceScope.Singleton)]
public sealed class FileSystemTorrentStorage(
    ILogger<FileSystemTorrentStorage> logger,
    IHostEnvironment environment) : ITorrentStorage
{
    public TorrentFileInfo Save(ReadOnlySpan<byte> data)
    {
        logger.LogTrace("begin save info to");
        var root = environment.ContentRootPath;
        var sub = Path.Combine(root, DateTimeOffset.UtcNow.ToString("yyyyMMdd"));
        if (!Directory.Exists(sub)) Directory.CreateDirectory(sub);
        var fileName = Convert.ToHexString(data);
        var filePath = Path.Combine(sub, fileName);
        using var fs = File.Create(filePath);
        fs.Write(data);
        fs.Flush(true);
        fs.Close();
        logger.LogTrace("end save info to");
        return new TorrentFileInfo
        {
            Announce = "",
            Info = TorrentFileDecode.DecodeInfo(data)
        };
    }

    public IEnumerable<TorrentFileInfo> Search(string file)
    {
        return [];
    }

    public TorrentFileInfo? Exists(ReadOnlySpan<byte> infohash)
    {
        return null;
    }
}