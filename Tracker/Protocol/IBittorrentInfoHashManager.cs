using System.Diagnostics.CodeAnalysis;

namespace Umi.Dht.Client.Protocol;

public interface IBittorrentInfoHashManager : IEnumerable<IBitTorrentInfoHash>, IDisposable
{
    IBitTorrentInfoHash this[string btih] { get; }

    int Count { get; }

    IBitTorrentInfoHash AddBitTorrentInfoHash(ReadOnlyMemory<byte> infoHash);

    bool TryGetBitTorrentInfoHash(string btih, [MaybeNullWhen(false)] out IBitTorrentInfoHash bitTorrentInfoHash);

    void TryReceiveInfoHashMetadata(string btih);
}