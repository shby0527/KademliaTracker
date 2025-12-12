using System.Security.Cryptography;
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
    public TorrentFileInfo Save(ReadOnlyMemory<byte> data)
    {
        try
        {
            logger.LogTrace("begin save info to");
            var root = environment.ContentRootPath;
            var sub = Path.Combine(root, DateTimeOffset.UtcNow.ToString("yyyyMMdd"));
            if (!Directory.Exists(sub)) Directory.CreateDirectory(sub);
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(data.ToArray());
            var fileName = Convert.ToHexString(hash);
            var filePath = Path.Combine(sub, $"{fileName}.torrent");
            using var fs = File.Create(filePath);
            var enumerator = data.Span.GetEnumerator();
            enumerator.MoveNext();
            Dictionary<string, object> torrent = new()
            {
                { "announce", "dht" },
                { "info", BEncoder.BDecodeToMap(ref enumerator) }
            };
            fs.Write(BEncoder.BEncode(torrent));
            fs.Flush(true);
            fs.Close();
            logger.LogTrace("end save info to");
            return new TorrentFileInfo
            {
                Announce = "",
                Info = TorrentFileDecode.DecodeInfo(data.Span)
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "save error");
            return default;
        }
    }

    public IEnumerable<TorrentFileInfo> Search(string file)
    {
        return [];
    }

    public TorrentFileInfo? Exists(ReadOnlyMemory<byte> infohash)
    {
        return null;
    }
}