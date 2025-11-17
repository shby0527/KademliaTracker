using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
            BucketDistance = 0
        });
    }


    public bool HasNodeExists(ReadOnlySpan<byte> node)
    {
        var distance = ComputeDistances(_currentNode.Span, node);
        var prefixLength = PrefixLength(distance);
        var bucket = FindNestDistanceBucket(prefixLength);
        return bucket.Value.HasNode(node);
    }

    public int AddNode(NodeInfo node)
    {
        try
        {
            _semaphore.WaitOne();
            var prefixLength = PrefixLength(node.Distance);
            var nearestBucket = this.FindNestDistanceBucket(prefixLength);
            nearestBucket.Value.InsertNode(node);
            // 这里判断 当前bucket 是否需要 分裂
            if (nearestBucket.Value is not { Count: > KBucket.MAX_BUCKET_NODE, BucketDistance: < 160 })
                return prefixLength;
            // 再判断，路由表是否满了，路由表一共  160 个空间
            _logger.LogDebug(" current k-bucket distance {distance}", nearestBucket.Value.BucketDistance);
            if (nearestBucket.Next is null) return prefixLength; // 有下一个桶，说明已经分裂过了
            var splitBucket = nearestBucket.Value.SplitBucket();
            _buckets.AddLast(splitBucket);
            return prefixLength;
        }
        finally
        {
            _semaphore.Release();
        }
    }


    public KBucket GetNestDistanceBucket(int distance)
    {
        return FindNestDistanceBucket(distance).Value;
    }


    private LinkedListNode<KBucket> FindNestDistanceBucket(int prefixLength)
    {
        var listNode = _buckets.Last;
        while (listNode is not null)
        {
            if (listNode.Value.BucketDistance <= prefixLength) return listNode;
            listNode = listNode.Previous;
        }

        // no found ? return the latest
        throw new UnreachableException("this can not happened");
    }


    public void AdjustNode(NodeInfo node)
    {
        var prefixLength = PrefixLength(node.Distance);
        var k = FindNestDistanceBucket(prefixLength);
        k.Value.AdjustItem(node);
    }

    public bool TryFoundNode(ReadOnlySpan<byte> node, [MaybeNullWhen(false)] out NodeInfo info)
    {
        var distances = ComputeDistances(node, _currentNode.Span);
        var prefixLength = PrefixLength(distances);
        var k = FindNestDistanceBucket(prefixLength);
        info = k.Value[node];
        return info != null;
    }

    public IEnumerable<NodeInfo> FindNodeList(ReadOnlySpan<byte> target)
    {
        var distances = ComputeDistances(target, _currentNode.Span);
        var bucket = this.FindNestDistanceBucket(PrefixLength(distances));
        return bucket.Value.Take(8);
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