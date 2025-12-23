using Umi.Dht.Client.TorrentIO.StorageInfo;

namespace Umi.Dht.Client.Commands;

public interface IKademliaCommand
{
    void ReBootstrap();

    long GetNodeCount();

    long GetBucketCount();

    IDictionary<string, (int Count, bool Received)> ListBitTorrentInfoHash();

    IDictionary<string, TorrentDirectoryInfo> ShowReceivedMetadata();
}