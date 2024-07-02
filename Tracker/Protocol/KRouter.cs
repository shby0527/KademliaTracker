using System.Collections.Concurrent;
using System.Diagnostics;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-路由表
/// </summary>
public class KRouter
{
    private const int MAX_BUCKET_NODE = 10;

    private readonly ConcurrentStack<KBucket> _buckets;

    private readonly ReadOnlyMemory<byte> _currentNode;

    public KRouter(ReadOnlyMemory<byte> currentNode)
    {
        _currentNode = currentNode;
        _buckets = new ConcurrentStack<KBucket>();
        _buckets.Push(new KBucket
        {
            BucketDistance = 0,
            Nodes = [],
            Split = false
        });
    }


    public bool HasNodeExists(ReadOnlySpan<byte> node)
    {
        var distance = KBucket.ComputeDistances(_currentNode.Span, node);
        var prefixLength = KBucket.PrefixLength(distance);
        var bucket = FindNestDistanceBucket(prefixLength);
        var idBytes = node.ToArray();
        return bucket.Nodes.Any(e => e.NodeID.Span.SequenceEqual(idBytes));
    }

    public void AddNode(NodeInfo node)
    {
        var prefixLength = KBucket.PrefixLength(node.Distance);
        var bucket = this.FindNestDistanceBucket(prefixLength);
        bucket.Nodes.Enqueue(node);
        if (bucket.Nodes.Count > MAX_BUCKET_NODE && bucket.BucketDistance < 160)
        {
            // TODO: Split the k-bucket
        }
    }


    private KBucket FindNestDistanceBucket(int prefixLength)
    {
        var enumerable = _buckets.Reverse();
        using var enumerator = enumerable.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.BucketDistance > prefixLength)
                continue;
            return enumerator.Current;
        }

        // no found ? return the latest
        throw new UnreachableException("this can not happened");
    }
}