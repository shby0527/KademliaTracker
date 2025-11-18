using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// Kademlia K-Bucket
/// </summary>
public class KBucket
{
    public const int MAX_BUCKET_NODE = 20;


    public required int[] BucketDistance { get; init; }

    private readonly LinkedList<NodeInfo> _nodeInfo = [];

    private readonly Semaphore _semaphore = new(1, 1);


    public NodeInfo? this[ReadOnlySpan<byte> id]
    {
        get
        {
            var bytes = id.ToArray();
            return _nodeInfo.FirstOrDefault(e => e.NodeId.Span.SequenceEqual(bytes));
        }
    }

    public IEnumerable<NodeInfo> Take(int num)
    {
        return [.._nodeInfo.Take(num)];
    }

    public void InsertNode(NodeInfo node)
    {
        try
        {
            _semaphore.WaitOne();
            _nodeInfo.AddFirst(node);
            // remove the last if max bucket
            if (_nodeInfo.Count > MAX_BUCKET_NODE * 4)
            {
                var last = _nodeInfo.Last;
                if (last is not null) _nodeInfo.Remove(last);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void AdjustItem(NodeInfo info)
    {
        try
        {
            _semaphore.WaitOne();
            // 调整节点到第一
            var node = _nodeInfo.Find(info);
            if (node is null) return;
            _nodeInfo.Remove(node);
            _nodeInfo.AddFirst(node);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool SplitBucket([MaybeNullWhen(false)] out KBucket bucket)
    {
        bucket = null;
        if (_nodeInfo.Count == 0) return false;
        if (this.BucketDistance[0] == this.BucketDistance[1]) return false;
        try
        {
            _semaphore.WaitOne();
            int maxPrefixLength = this.BucketDistance[1];
            if (this.BucketDistance[0] == maxPrefixLength / 2) return false;
            this.BucketDistance[1] = maxPrefixLength / 2;
            // 基础判断
            bucket = new KBucket
            {
                BucketDistance = [maxPrefixLength / 2, maxPrefixLength],
            };
            var node = _nodeInfo.First;
            while (node is not null)
            {
                var current = node;
                node = current.Next;
                var prefixLength = KRouter.PrefixLength(current.Value.Distance);
                if (prefixLength >= this.BucketDistance[0] && prefixLength < this.BucketDistance[1])
                    continue; // else case , 无操作，节点留在当前
                _nodeInfo.Remove(current);
                bucket._nodeInfo.AddLast(current);
            }

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool HasNode(ReadOnlySpan<byte> nodeId)
    {
        var bytes = nodeId.ToArray();
        return _nodeInfo.Any(t => t.NodeId.Span.SequenceEqual(bytes));
    }

    public long Count => _nodeInfo.Count;
}