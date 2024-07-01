using System.Collections.Concurrent;
using System.Numerics;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// Kademlia K-Bucket
/// </summary>
public class KBucket
{
    public required BigInteger BucketDistance { get; init; }

    public required ConcurrentQueue<NodeInfo> Nodes { get; init; }

    public required bool Split { get; init; }

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
}