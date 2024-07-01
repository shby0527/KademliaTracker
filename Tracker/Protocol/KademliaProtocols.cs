using System.Text;
using Umi.Dht.Client.Utils;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// KAD 网络的协议包
/// </summary>
public static class KademliaProtocols
{
    /// <summary>
    /// Kademlia find node 包
    /// </summary>
    /// <param name="id">我的ID</param>
    /// <param name="target">目标ID</param>
    /// <returns>返回的包</returns>
    public static KRpcPackage FindNode(ReadOnlySpan<byte> id, ReadOnlySpan<byte> target)
    {
        return new KRpcPackage
        {
            Type = KRpcTypes.Query,
            TransactionId = StringUtils.GenerateRandomString(8),
            Query = new QueryPackage
            {
                Method = "find_node",
                Arguments = new Dictionary<string, object>
                {
                    { "id", new ReadOnlyMemory<byte>(id.ToArray()) },
                    { "target", new ReadOnlyMemory<byte>(target.ToArray()) }
                }
            }
        };
    }


    /// <summary>
    /// Kademlia Ping Node
    /// </summary>
    /// <param name="id">ID 包</param>
    /// <returns>返回的ping包</returns>
    public static KRpcPackage Ping(ReadOnlySpan<byte> id)
    {
        return new KRpcPackage
        {
            Type = KRpcTypes.Query,
            TransactionId = StringUtils.GenerateRandomString(8),
            Query = new QueryPackage
            {
                Method = "ping",
                Arguments = new Dictionary<string, object>
                {
                    { "id", new ReadOnlyMemory<byte>(id.ToArray()) }
                }
            }
        };
    }
}