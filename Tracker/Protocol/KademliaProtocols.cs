using System.Security.Cryptography;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// KAD 网络的协议包
/// </summary>
public static class KademliaProtocols
{
    private static readonly TokenGenerator _tokenGenerator = new();


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
            TransactionId = RandomNumberGenerator.GetBytes(8),
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
            TransactionId = RandomNumberGenerator.GetBytes(8),
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

    public static KRpcPackage GetPeersRequest(ReadOnlySpan<byte> id, ReadOnlySpan<byte> hash)
    {
        return new KRpcPackage
        {
            Type = KRpcTypes.Query,
            TransactionId = RandomNumberGenerator.GetBytes(8),
            Query = new QueryPackage
            {
                Method = "get_peers",
                Arguments = new Dictionary<string, object>
                {
                    { "id", id.ToArray() },
                    { "info_hash", hash.ToArray() }
                }
            }
        };
    }

    public static KRpcPackage PingResponse(ReadOnlySpan<byte> id, ReadOnlySpan<byte> transactionId)
    {
        return new KRpcPackage
        {
            Type = KRpcTypes.Response,
            TransactionId = transactionId.ToArray(),
            Response = new Dictionary<string, object>
            {
                { "id", id.ToArray() }
            }
        };
    }


    public static KRpcPackage FindNodeResponse(ReadOnlySpan<byte> id,
        ReadOnlySpan<byte> nodes,
        ReadOnlySpan<byte> transactionId)
    {
        return new KRpcPackage
        {
            Type = KRpcTypes.Response,
            TransactionId = transactionId.ToArray(),
            Response = new Dictionary<string, object>
            {
                { "id", id.ToArray() },
                { "nodes", nodes.ToArray() }
            }
        };
    }

    public static KRpcPackage GetPeersResponse(ReadOnlySpan<byte> id,
        ReadOnlySpan<byte> nodes,
        ICollection<byte[]> peers,
        ReadOnlySpan<byte> transactionId)
    {
        var token = _tokenGenerator.Token.ToArray();
        var dictionary = new Dictionary<string, object>
        {
            { "id", id.ToArray() },
            { "nodes", nodes.ToArray() },
            { "token", token }
        };
        if (peers.Count != 0)
        {
            dictionary.Add("values", peers);
        }

        return new KRpcPackage
        {
            Type = KRpcTypes.Response,
            TransactionId = transactionId.ToArray(),
            Response = dictionary
        };
    }
}