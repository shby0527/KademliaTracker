using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.TorrentIO;

public interface IMagnetLinkStorage
{
    /// <summary>
    /// 查找一个 magnet 连接
    /// </summary>
    /// <param name="hash">hash</param>
    /// <returns>返回找到的hash</returns>
    /// <exception cref="KeyNotFoundException">链接找不到</exception>
    MagnetInfo FoundMagnet(ReadOnlySpan<byte> hash);

    /// <summary>
    /// 存储一个链接
    /// </summary>
    /// <param name="magnet">磁力参数</param>
    /// <returns>成功存储</returns>
    bool StoreMagnet(MagnetInfo magnet);

    /// <summary>
    /// 查找一个 magnet 连接
    /// </summary>
    /// <param name="hash">hash</param>
    /// <returns>返回找到的hash</returns>
    /// <exception cref="KeyNotFoundException">链接找不到</exception>
    /// <see cref="FoundMagnet"/>
    MagnetInfo this[ReadOnlySpan<byte> hash] => FoundMagnet(hash);
}