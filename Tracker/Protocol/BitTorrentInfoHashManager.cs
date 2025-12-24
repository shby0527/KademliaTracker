using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Bittorrent;

namespace Umi.Dht.Client.Protocol;

[Service(ServiceScope.Singleton)]
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BitTorrentInfoHashManager(IServiceProvider provider) : IBittorrentInfoHashManager
{
    private readonly ILogger<BitTorrentInfoHashManager> _logger =
        provider.GetRequiredService<ILogger<BitTorrentInfoHashManager>>();

    private readonly Dictionary<string, IBitTorrentInfoHash> _bitTorrentInfo = new();

    public IBitTorrentInfoHash this[string btih] => _bitTorrentInfo[btih];

    public int Count => _bitTorrentInfo.Count;

    public IBitTorrentInfoHash AddBitTorrentInfoHash(ReadOnlyMemory<byte> infoHash)
    {
        var btih = Convert.ToHexString(infoHash.ToArray());
        lock (_bitTorrentInfo)
        {
            if (_bitTorrentInfo.TryGetValue(btih, out var hash)) return hash;
            hash = new BitTorrentInfoHashPrivateTracker(infoHash.ToArray(),
                provider.GetRequiredService<ILogger<BitTorrentInfoHashPrivateTracker>>(),
                provider.CreateScope());
            _bitTorrentInfo[btih] = hash;
            return hash;
        }
    }

    public bool TryGetBitTorrentInfoHash(string btih, [MaybeNullWhen(false)] out IBitTorrentInfoHash bitTorrentInfoHash)
    {
        _logger.LogTrace("try get btih {btih} info", btih);
        return _bitTorrentInfo.TryGetValue(btih, out bitTorrentInfoHash);
    }

    public static IPeer CreatePeer(IPAddress address, int port, NodeInfo node, byte flags)
    {
        return new PeerInfoPrivateTracker(address, port, node, flags);
    }

    public void TryReceiveInfoHashMetadata(string btih)
    {
        // 尝试开始获取info hash 的 metadata， 可能的
        if (_bitTorrentInfo.TryGetValue(btih, out var hash))
        {
            hash.BeginGetMetadata()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }

    public IEnumerator<IBitTorrentInfoHash> GetEnumerator()
    {
        return _bitTorrentInfo.Select(hash => hash.Value).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    public void Dispose()
    {
        foreach (var bitTorrentInfoHash in _bitTorrentInfo)
        {
            bitTorrentInfoHash.Value.Dispose();
        }
    }

    private class PeerInfoPrivateTracker(IPAddress address, int port, NodeInfo node, byte flags) : IPeer
    {
        public NodeInfo Node => node;
        public IPAddress Address => address;
        public int Port => port;

        public byte Flags => flags;
    }
}