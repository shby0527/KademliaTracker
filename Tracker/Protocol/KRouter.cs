using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-路由表
/// </summary>
public class KRouter
{
    private const int MAX_BUCKET_NODE = 10;

    private readonly ConcurrentStack<KBucket> _buckets;

    private readonly ReadOnlyMemory<byte> _currentNode;

    private readonly ILogger<KRouter> _logger;

    private readonly Semaphore _semaphore;

    public KRouter(ReadOnlyMemory<byte> currentNode, IServiceProvider provider)
    {
        _semaphore = new Semaphore(1, 1);
        _logger = provider.GetRequiredService<ILogger<KRouter>>();
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

    public int AddNode(NodeInfo node)
    {
        try
        {
            _semaphore.WaitOne();
            var prefixLength = KBucket.PrefixLength(node.Distance);
            var bucket = this.FindNestDistanceBucket(prefixLength);
            bucket.Nodes.Enqueue(node);

            if (bucket.Nodes.Count > MAX_BUCKET_NODE && bucket.BucketDistance < 160)
            {
                if (_buckets.TryPeek(out var latest) && latest.BucketDistance != bucket.BucketDistance)
                {
                    _logger.LogTrace("Split K-Bucket and next length {len}", bucket.BucketDistance + 1);
                    var nextBkt = new KBucket
                    {
                        BucketDistance = latest.BucketDistance + 1,
                        Nodes = [],
                        Split = false
                    };
                    for (var i = 0; i < latest.Nodes.Count; i++)
                    {
                        if (!latest.Nodes.TryDequeue(out var info)) continue;
                        var length = KBucket.PrefixLength(info.Distance);
                        if (length < nextBkt.BucketDistance)
                        {
                            latest.Nodes.Enqueue(info);
                        }
                        else
                        {
                            nextBkt.Nodes.Enqueue(info);
                        }
                    }
                }
            }

            if (LatestPrefixLength < prefixLength)
            {
                LatestPrefixLength = prefixLength;
            }

            return prefixLength;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public int LatestPrefixLength { get; private set; } = 0;


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


    public long PeersCount
    {
        get { return _buckets.Aggregate<KBucket, long>(0, (current, bucket) => current + bucket.Nodes.Count); }
    }
}