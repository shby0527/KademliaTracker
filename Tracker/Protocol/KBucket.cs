using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// Kademlia K-Bucket
/// </summary>
public class KBucket : IDisposable
{
    public const int MAX_BUCKET_NODE = 20;

    public required BigInteger[] BucketDistance { get; init; }

    private readonly LinkedList<NodeInfo> _nodeInfo = [];

    private readonly Semaphore _semaphore = new(1, 1);

    private DateTimeOffset _latestUpdateTime = DateTimeOffset.UtcNow;

    public bool IsFresh => _latestUpdateTime + TimeSpan.FromMinutes(15) < DateTimeOffset.UtcNow;

    private readonly Timer _nodeChecker;

    public KBucket()
    {
        _nodeChecker = new Timer(this.CheckNode, this, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
    }


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

            _latestUpdateTime = DateTimeOffset.UtcNow;
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
            _latestUpdateTime = DateTimeOffset.UtcNow;
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
            BigInteger maxDist = this.BucketDistance[1];
            if (this.BucketDistance[0] == maxDist / 2) return false;
            this.BucketDistance[1] = maxDist / 2;
            // 基础判断
            bucket = new KBucket
            {
                BucketDistance = [maxDist / 2, maxDist],
            };
            var node = _nodeInfo.First;
            while (node is not null)
            {
                var current = node;
                node = current.Next;
                if (current.Value.Distance >= this.BucketDistance[0] && current.Value.Distance < this.BucketDistance[1])
                    continue; // else case , 无操作，节点留在当前
                _nodeInfo.Remove(current);
                bucket._nodeInfo.AddLast(current);
            }

            _latestUpdateTime = DateTimeOffset.UtcNow;
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

    private void CheckNode(object? stats)
    {
        var node = _nodeInfo.First;
        while (node is not null)
        {
            if (node.Value is { Healthy: NodeHealth.Questionable })
            {
                // 需要发送ping 确认的节点, 由事件处理
                this.UnhealthNodeChecker?.Invoke(this, node.Value);
            }

            node = node.Next;
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _nodeChecker.Dispose();
    }

    public void Refresh()
    {
        // 对所有节点 发送 ping
        var node = _nodeInfo.First;
        while (node is not null)
        {
            this.UnhealthNodeChecker?.Invoke(this, node.Value);
            node = node.Next;
        }
    }

    public event NodeCheckerEventHandler? UnhealthNodeChecker;
}

public delegate void NodeCheckerEventHandler(KBucket bucket, NodeInfo node);