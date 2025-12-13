namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeerFactory
{
    IBittorrentPeer CreatePeer(IPeer peer, ReadOnlyMemory<byte> bittorrentInfoHash);
}