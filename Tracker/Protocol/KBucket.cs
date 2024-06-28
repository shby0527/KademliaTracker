namespace Umi.Dht.Client.Protocol;

/// <summary>
/// Kademlia K-Bucket
/// </summary>
public class KBucket
{
    public required int BucketDistance { get; init; }

    public required IList<NodeInfo> Nodes { get; init; }
}