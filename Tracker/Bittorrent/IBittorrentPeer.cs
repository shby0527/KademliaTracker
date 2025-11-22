using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Bittorrent;

public interface IBittorrentPeer : IPeer
{
    bool IsConnected { get; }

    void Connect();


    void Disconnect();

    ReadOnlySpan<byte> GetHashMetadata(long piece);
}