using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-路由表
/// </summary>
public partial class KRouter : IDisposable
{
    private readonly LinkedList<KBucket> _buckets;

    private readonly ReadOnlyMemory<byte> _currentNode;

    private readonly ILogger<KRouter> _logger;

    private readonly Semaphore _semaphore;

    private readonly Timer _refreshBucketTimer;

    public KRouter(ReadOnlyMemory<byte> currentNode, IServiceProvider provider)
    {
        _semaphore = new Semaphore(1, 1);
        _logger = provider.GetRequiredService<ILogger<KRouter>>();
        _currentNode = currentNode;
        _buckets = [];
        var bucket = new KBucket
        {
            BucketDistance = [0, BigInteger.One << 160]
        };
        bucket.UnhealthNodeChecker += OnUnhealthNodeChecked;
        _buckets.AddLast(bucket);
        _refreshBucketTimer = new Timer(this.CheckBuckets, this, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
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
            splitBucket.UnhealthNodeChecker += OnUnhealthNodeChecked;
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
        Debug.Assert(_buckets.Last is not null, "buckets latest is null");
        return _buckets.Last;
    }


    public void AdjustNode(NodeInfo node)
    {
        var k = FindNestDistanceBucket(node.Distance);
        k.Value.AdjustItem(node);
    }

    public bool TryFoundNode(ReadOnlySpan<byte> node, [MaybeNullWhen(false)] out NodeInfo info)
    {
        try
        {
            var distances = ComputeDistances(node, _currentNode.Span);
            var k = FindNestDistanceBucket(distances);
            info = k.Value[node];
            return info != null;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "some error throws");
            info = null;
            return false;
        }
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


    private void CheckBuckets(object? stats)
    {
        var node = _buckets.First;
        while (node is not null)
        {
            if (!node.Value.IsFresh)
            {
                node.Value.Refresh();
                this.BuckerRefreshing?.Invoke(this, node.Value);
            }

            node = node.Next;
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _refreshBucketTimer.Dispose();
        var node = _buckets.First;
        while (node is not null)
        {
            node.Value.UnhealthNodeChecker -= OnUnhealthNodeChecked;
            node.Value.Dispose();
            var next = node.Next;
            _buckets.Remove(node);
            node = next;
        }
    }

    /// <summary>
    /// only event transfer , 事件转发
    /// </summary>
    /// <param name="bucket"></param>
    /// <param name="node"></param>
    private void OnUnhealthNodeChecked(KBucket bucket, NodeInfo node)
    {
        this.UnhealthNodeChecker?.Invoke(bucket, node);
    }

    public event NodeCheckerEventHandler? UnhealthNodeChecker;

    public event BucketRefreshEventHandler? BuckerRefreshing;
}

public delegate void BucketRefreshEventHandler(KRouter sender, KBucket bucket);