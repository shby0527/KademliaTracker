using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;

namespace Umi.Dht.Client.Bittorrent.Sample;

[Service(ServiceScope.Scoped)]
public sealed class SamplePeerFactory : IBittorrentPeerFactory
{
    private readonly ReadOnlyMemory<byte> _peerId;

    private readonly ILogger<SamplePeerFactory> _logger;

    private readonly IServiceProvider _provider;

    public SamplePeerFactory(ILogger<SamplePeerFactory> logger, IServiceProvider provider)
    {
        var generator = RandomNumberGenerator.Create();
        byte[] peerId = new byte[20];
        generator.GetBytes(peerId);
        _peerId = peerId;
        _logger = logger;
        _provider = provider;
    }

    public IBittorrentPeer CreatePeer(IPeer peer, ReadOnlyMemory<byte> bittorrentInfoHash)
    {
        _logger.LogTrace("begin create sample peer, {addr}:{port}", peer.Address, peer.Port);
        return new SamplePeer(_provider, peer, bittorrentInfoHash.ToArray(), _peerId.ToArray());
    }
}