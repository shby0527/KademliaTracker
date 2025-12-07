using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.TorrentIO;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Plugins.FileSystemStorage;

[Service(ServiceScope.Singleton)]
public sealed class FileSystemMagnetLinkStorage(
    ILogger<FileSystemMagnetLinkStorage> logger,
    IHostEnvironment environment) : IMagnetLinkStorage
{
    public MagnetInfo FoundMagnet(ReadOnlySpan<byte> hash)
    {
        // search magnetInfo not implement , direct return
        return new MagnetInfo
        {
            Hash = hash
        };
    }

    public bool StoreMagnet(MagnetInfo magnet)
    {
        // store sub dictionary
        var path = environment.ContentRootPath;
        var s = Path.Combine(path, "magnet");
        var di = new DirectoryInfo(s);
        if (!di.Exists)
        {
            di.Create();
        }

        var s1 = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        FileInfo m = new(Path.Combine(s, $"{s1}.txt"));
        using var sw = m.Exists ? m.AppendText() : m.CreateText();
        StringBuilder sb = new($"magnet:?xt=urn:btih:{Convert.ToHexString(magnet.Hash)}");
        if (!string.IsNullOrEmpty(magnet.DisplayName))
        {
            sb.Append($"&dn={magnet.DisplayName}");
        }

        if (magnet.ExactLength is not null)
        {
            sb.Append($"xl={magnet.ExactLength}");
        }

        if (magnet.Trackers is not null)
        {
            foreach (var tracker in magnet.Trackers)
            {
                sb.AppendJoin('&', $"tr={tracker}");
            }
        }

        sw.WriteLine(sb.ToString());
        return true;
    }
}