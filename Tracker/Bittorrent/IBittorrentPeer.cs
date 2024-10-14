using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeer
{
    IPeer Peer { get; }

    bool IsConnected { get; }

    void Connect();

    void Disconnect();

    ReadOnlySpan<byte> GetHashMetadata();
}