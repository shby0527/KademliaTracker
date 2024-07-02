using System.Net;
using System.Numerics;

namespace Umi.Dht.Client.Protocol;

public class NodeInfo
{
    public required ReadOnlyMemory<byte> NodeID { get; init; }

    public required BigInteger Distance { get; init; }

    public required IPAddress NodeAddress { get; init; }

    public int NodePort { get; init; }

    public DateTimeOffset LatestAccessTime { get; set; }
}