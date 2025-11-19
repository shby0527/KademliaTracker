using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-路由表
/// </summary>
public partial class KRouter
{
    private readonly LinkedList<KBucket> _buckets;

    private readonly ReadOnlyMemory<byte> _currentNode;

    private readonly ILogger<KRouter> _logger;

    private readonly Semaphore _semaphore;

    public KRouter(ReadOnlyMemory<byte> currentNode, IServiceProvider provider)
    {
        _semaphore = new Semaphore(1, 1);
        _logger = provider.GetRequiredService<ILogger<KRouter>>();
        _currentNode = currentNode;
        _buckets = [];
        _buckets.AddLast(new KBucket
        {
            BucketDistance = [0, BigInteger.One << 160]
        });
    }


    public bool HasNodeExists(ReadOnlySpan<byte> node)
    {
        var distance = ComputeDistances(_currentNode.Span, node);
        var bucket = FindNestDistanceBucket(distance);
        return bucket.Value.HasNode(node);
    }

    public int AddNode(NodeInfo node)
    {
        try
        {
            _semaphore.WaitOne();
            var nearestBucket = this.FindNestDistanceBucket(node.Distance);
            var prefixLength = PrefixLength(node.Distance);
            nearestBucket.Value.InsertNode(node);
            // 这里判断 当前bucket 是否需要 分裂
            if (nearestBucket.Value.Count <= KBucket.MAX_BUCKET_NODE &&
                ReferenceEquals(nearestBucket.Value, _buckets.First!.Value))
                return prefixLength;
            // 再判断，路由表是否满了，路由表一共  160 个空间
            _logger.LogDebug(" current k-bucket distance {distance}", nearestBucket.Value.BucketDistance);
            var splitSuccess = nearestBucket.Value.SplitBucket(out var splitBucket);
            if (!splitSuccess || splitBucket is null) return prefixLength;
            _buckets.AddAfter(nearestBucket, splitBucket);
            return prefixLength;
        }
        finally
        {
            _semaphore.Release();
        }
    }


    public KBucket GetNestDistanceBucket(BigInteger distance)
    {
        return FindNestDistanceBucket(distance).Value;
    }


    private LinkedListNode<KBucket> FindNestDistanceBucket(BigInteger dist)
    {
        var listNode = _buckets.First;
        while (listNode is not null)
        {
            if (dist >= listNode.Value.BucketDistance[0] && dist < listNode.Value.BucketDistance[1])
                return listNode;
            listNode = listNode.Next;
        }

        // no found ? return the latest
        return _buckets.Last!;
    }


    public void AdjustNode(NodeInfo node)
    {
        var k = FindNestDistanceBucket(node.Distance);
        k.Value.AdjustItem(node);
    }

    public bool TryFoundNode(ReadOnlySpan<byte> node, [MaybeNullWhen(false)] out NodeInfo info)
    {
        var distances = ComputeDistances(node, _currentNode.Span);
        var k = FindNestDistanceBucket(distances);
        info = k.Value[node];
        return info != null;
    }

    public IEnumerable<NodeInfo> FindNodeList(ReadOnlySpan<byte> target)
    {
        var distances = ComputeDistances(target, _currentNode.Span);
        var bucket = this.FindNestDistanceBucket(distances);
        return bucket.Value.Take(8);
    }

    public IDictionary<int[], long> GetRouterAvg()
    {
        Dictionary<int[], long> result = new Dictionary<int[], long>();
        foreach (var node in _buckets)
        {
            result.Add([PrefixLength(node.BucketDistance[0]), PrefixLength(node.BucketDistance[1])], node.Count);
        }

        return result;
    }

    /// <summary>
    /// 统计节点数
    /// </summary>
    public long NodeCount
    {
        get { return _buckets.Sum(bucket => bucket.Count); }
    }

    /// <summary>
    /// k桶数
    /// </summary>
    public long KBucketsCount => _buckets.Count;
}