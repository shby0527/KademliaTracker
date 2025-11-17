using System.Diagnostics;
using System.Net;
using System.Numerics;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// 节点信息
/// </summary>
public class NodeInfo
{
    private const ushort MAX_RETRY_COUNT = 5;

    private readonly TimeSpan MAX_TIME_OUT = TimeSpan.FromMinutes(15);

    public required ReadOnlyMemory<byte> NodeId { get; init; }

    public required BigInteger Distance { get; init; }

    public required IPAddress NodeAddress { get; init; }

    public int NodePort { get; init; }

    public DateTimeOffset LatestAccessTime { get; private set; } = DateTimeOffset.UtcNow;

    private uint _retryCount = 0;

    public KRpcPackage GeneratePingPackage()
    {
        _retryCount++;
        return KademliaProtocols.Ping(NodeId.Span);
    }

    public void Refresh()
    {
        _retryCount = 0;
        LatestAccessTime = DateTimeOffset.UtcNow;
    }

    public NodeHealth Healthy
    {
        get
        {
            if (_retryCount >= MAX_RETRY_COUNT) return NodeHealth.Unhealthy;
            return LatestAccessTime <= DateTimeOffset.UtcNow + MAX_TIME_OUT
                ? NodeHealth.Healthy
                : NodeHealth.Questionable;
        }
    }
}

/// <summary>
/// 节点健康状态
/// </summary>
public enum NodeHealth : byte
{
    /// <summary>
    /// 良好
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 可疑节点
    /// </summary>
    Questionable,

    /// <summary>
    /// 不好
    /// </summary>
    Unhealthy,
}