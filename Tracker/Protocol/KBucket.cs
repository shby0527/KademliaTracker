namespace Umi.Dht.Client.Protocol;

/// <summary>
/// Kademlia K-Bucket
/// </summary>
public class KBucket
{
    public required int BucketDistance { get; init; }

    public required Queue<NodeInfo> Nodes { get; init; }

    public required bool Split { get; init; }
}