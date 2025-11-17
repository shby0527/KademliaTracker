using System.Numerics;

namespace Umi.Dht.Client.Protocol;

public partial class KRouter
{
    /// <summary>
    /// 计算距离
    /// </summary>
    /// <param name="h1">info hash 1</param>
    /// <param name="h2">info hash 2</param>
    /// <returns>距离</returns>
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
}