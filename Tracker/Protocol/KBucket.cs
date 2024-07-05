using System.Collections.Concurrent;
using System.Numerics;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// Kademlia K-Bucket
/// </summary>
public class KBucket
{
    public required int BucketDistance { get; init; }

    public required Stack<NodeInfo> Nodes { get; init; }


    private readonly Semaphore _semaphore = new(1, 1);

    public static BigInteger ComputeDistances(ReadOnlySpan<byte> h1, ReadOnlySpan<byte> h2)
    {
        if (h1.Length != h2.Length) return BigInteger.Zero;
        Span<byte> buffer = stackalloc byte[h1.Length];
        for (var i = 0; i < h1.Length; i++)
        {
            buffer[i] = (byte)(h1[i] ^ h2[i]);
        }

        return new BigInteger(buffer, true, true);
    }

    /// <summary>
    /// 计算前缀长度
    /// </summary>
    /// <param name="node">节点</param>
    /// <returns>前缀</returns>
    public static int PrefixLength(BigInteger node)
    {
        var mask = BigInteger.One << 160;
        for (var i = 0; i < 160; i++)
        {
            if ((node & mask) != BigInteger.Zero) return i;
            mask |= mask >> 1;
        }

        return 160;
    }

    public void InsertNode(NodeInfo node)
    {
        try
        {
            _semaphore.WaitOne();
            this.Nodes.Push(node);
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
            for (var i = 0; i < this.Nodes.Count; i++)
            {
                var pop = this.Nodes.Pop();
                if (ReferenceEquals(pop, info)) continue;
                this.Nodes.Push(pop);
            }

            info.LatestAccessTime = DateTimeOffset.Now;
            this.Nodes.Push(info);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public KBucket SplitBucket(ReadOnlySpan<byte> nodeId)
    {
        var bucket = new KBucket
        {
            BucketDistance = this.BucketDistance + 1,
            Nodes = []
        };
        try
        {
            _semaphore.WaitOne();
            for (var i = 0; i < this.Nodes.Count; i++)
            {
                var info = this.Nodes.Pop();
                if (PrefixLength(info.Distance) < bucket.BucketDistance)
                {
                    this.Nodes.Push(info);
                }
                else
                {
                    bucket.Nodes.Push(info);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }


        return bucket;
    }
}