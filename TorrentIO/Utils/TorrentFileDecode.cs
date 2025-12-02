using System.Collections.Immutable;
using System.Text;
using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.TorrentIO.Utils;

/// <summary>
/// 种子文件解析
/// </summary>
public static class TorrentFileDecode
{
    /// <summary>
    /// 解析BT种子文件
    /// </summary>
    /// <param name="data">种子数据文件</param>
    /// <returns>种子解析后的结果</returns>
    /// <exception cref="FormatException">格式错误</exception>
    public static TorrentFileInfo Decode(ReadOnlySpan<byte> data)
    {
        var dataBuffer = data.GetEnumerator();
        if (!dataBuffer.MoveNext()) throw new FormatException("can not decode empty file");
        var dic = BEncoder.BDecodeToMap(ref dataBuffer);
        var innerInfoDir = ConvertOrThrowWhenNotFound<IDictionary<string, object>, FormatException>(dic, "info");

        return new TorrentFileInfo
        {
            Announce = ConvertToStringOrThrowWhenNotFound<FormatException>(dic, "announce", Encoding.UTF8),
            Info = new TorrentDirectoryInfo
            {
                Name = ConvertToStringOrThrowWhenNotFound<FormatException>(innerInfoDir, "name", Encoding.UTF8),
                PieceLength = ConvertOrThrowWhenNotFound<long, FormatException>(innerInfoDir, "piece length"),
                Pieces = ConvertToBytesList(ConvertToSpanOrThrowWhenNotFound<FormatException>(innerInfoDir, "pieces")),
                Length = ConvertTo<long?>(innerInfoDir, "length"),
                Files = ConvertTo<IEnumerable<object>>(innerInfoDir, "files")?.Select(e =>
                    new TorrentFileList
                    {
                        Length = ConvertOrThrowWhenNotFound<long, FormatException>((IDictionary<string, object>)e,
                            "length"),
                        Path = Path.Combine(
                            ConvertOrThrowWhenNotFound<IEnumerable<object>, FormatException>(
                                    (IDictionary<string, object>)e, "path")
                                .Select(p => Encoding.UTF8.GetString((byte[])p)).ToArray())
                    }),
            },
            Comment = ConvertToString(dic, "comment", Encoding.UTF8),
            CreatedBy = ConvertToString(dic, "created by", Encoding.UTF8),
            CreationDate = ConvertTo<long>(dic, "creation date"),
            Encoding = ConvertToString(dic, "encoding", Encoding.UTF8),
            UrlList = ConvertTo<IEnumerable<object>>(dic, "url-list")?.Select(p => Encoding.UTF8.GetString((byte[])p)),
            AnnounceList = ConvertTo<IEnumerable<object>>(dic, "announce-list")?.Select(p =>
                ((IEnumerable<object>)p).Select(y => Encoding.UTF8.GetString((byte[])y))),
        };
    }

    public static TorrentDirectoryInfo DecodeInfo(ReadOnlySpan<byte> data)
    {
        var dataBuffer = data.GetEnumerator();
        if (!dataBuffer.MoveNext()) throw new FormatException("can not decode empty file");
        var dic = BEncoder.BDecodeToMap(ref dataBuffer);
        return new TorrentDirectoryInfo
        {
            Name = ConvertToStringOrThrowWhenNotFound<FormatException>(dic, "name", Encoding.UTF8),
            PieceLength = ConvertOrThrowWhenNotFound<long, FormatException>(dic, "piece length"),
            Pieces = ConvertToBytesList(ConvertToSpanOrThrowWhenNotFound<FormatException>(dic, "pieces")),
            Length = ConvertTo<long?>(dic, "length"),
            Files = ConvertTo<IEnumerable<object>>(dic, "files")?.Select(e =>
                new TorrentFileList
                {
                    Length = ConvertOrThrowWhenNotFound<long, FormatException>((IDictionary<string, object>)e,
                        "length"),
                    Path = Path.Combine(
                        ConvertOrThrowWhenNotFound<IEnumerable<object>, FormatException>(
                                (IDictionary<string, object>)e, "path")
                            .Select(p => Encoding.UTF8.GetString((byte[])p)).ToArray())
                }),
        };
    }

    private static IEnumerable<byte[]> ConvertToBytesList(ReadOnlySpan<byte> data)
    {
        List<byte[]> rts = [];
        for (int i = 0; i < data.Length / 20; i++)
        {
            rts.Add(data.Slice(i * 20, 20).ToArray());
        }

        return rts.ToImmutableList();
    }

    private static string ConvertToStringOrThrowWhenNotFound<T>(IDictionary<string, object> dic,
        string key, Encoding encoding)
        where T : Exception, new()
    {
        return !dic.TryGetValue(key, out var result) ? throw new T() : encoding.GetString((byte[])result);
    }

    private static TR ConvertOrThrowWhenNotFound<TR, T>(IDictionary<string, object> dic, string key)
        where T : Exception, new()
    {
        if (!dic.TryGetValue(key, out var result)) throw new T();

        return (TR)result;
    }

    private static ReadOnlySpan<byte> ConvertToSpanOrThrowWhenNotFound<T>(IDictionary<string, object> dic, string key)
        where T : Exception, new()
    {
        if (!dic.TryGetValue(key, out var result)) throw new T();
        return (byte[])result;
    }

    private static string? ConvertToString(IDictionary<string, object> dic, string key, Encoding encoding)
    {
        return !dic.TryGetValue(key, out var result) ? null : encoding.GetString((byte[])result);
    }

    private static TR? ConvertTo<TR>(IDictionary<string, object> dic, string key)
    {
        if (!dic.TryGetValue(key, out var result)) return default;

        return (TR)result;
    }
}