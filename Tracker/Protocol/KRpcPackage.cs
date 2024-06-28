using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-RPC 协议格式 
/// </summary>
public struct KRpcPackage
{
    /// <summary>
    /// transaction Id ，事务ID，响应的相关请求的ID
    /// </summary>
    public string TransactionId { get; init; }

    /// <summary>
    /// 响应类型, 每个类型对应下面任意一个字段
    /// </summary>
    public KRpcTypes Type { get; init; }

    /// <summary>
    /// t = q
    /// </summary>
    public QueryPackage? Query { get; set; }

    /// <summary>
    /// r
    /// </summary>
    public IDictionary<string, object>? Response { get; set; }

    /// <summary>
    /// e, list
    /// </summary>
    public (int Code, string Message)? Error { get; set; }

    public ReadOnlySpan<byte> Encode()
    {
        Dictionary<string, object> package = new()
        {
            { "t", TransactionId }
        };
        package["y"] = Type switch
        {
            KRpcTypes.Query => "q",
            KRpcTypes.Response => "r",
            KRpcTypes.Error => "e",
            _ => throw new ArgumentOutOfRangeException(paramName: null, message: "Type Out of Range")
        };
        switch (Type)
        {
            case KRpcTypes.Query:
                if (Query == null) throw new ArgumentNullException(paramName: null, message: "Query is null");
                var v = Query.Value;
                package["q"] = v.Method;
                package["a"] = v.Arguments;
                break;
            case KRpcTypes.Response:
                package["r"] = Response
                               ?? throw new ArgumentNullException(paramName: null, message: "Response is null");
                break;
            case KRpcTypes.Error:
                if (Error == null) throw new ArgumentNullException(paramName: null, message: "Error is null");
                package["e"] = new object[]
                {
                    Error.Value.Code,
                    Error.Value.Message
                };
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return Encoding.UTF8.GetBytes(BEncode(package));
    }

    public static KRpcPackage Decode(ReadOnlySpan<byte> buffer)
    {
        return default;
    }


    private static string BEncode(int number)
    {
        return $"i{number}e";
    }

    private static string BEncode(string str)
    {
        return $"{str.Length}:{str}";
    }

    private static string BEncode(IDictionary<string, object> dic)
    {
        var sb = new StringBuilder();
        sb.Append('d');
        foreach (var item in dic.ToImmutableSortedDictionary(StringComparer.Ordinal))
        {
            sb.Append(BEncode(item.Key));
            sb.Append(BEncodeTypeMap(item.Value));
        }

        sb.Append('e');
        return sb.ToString();
    }

    private static string BEncode(ICollection<object> list)
    {
        var sb = new StringBuilder();
        sb.Append('l');
        foreach (var o in list)
        {
            sb.Append(BEncodeTypeMap(o));
        }

        sb.Append('e');
        return sb.ToString();
    }

    private static string BEncodeTypeMap(object o)
    {
        return o switch
        {
            int value => BEncode(value),
            string str => BEncode(str),
            ICollection<object> list => BEncode(list),
            IDictionary<string, object> iDic => BEncode(iDic),
            _ => ""
        };
    }
}

public enum KRpcTypes : byte
{
    /// <summary>
    /// t = q
    /// </summary>
    Query,

    /// <summary>
    /// t = r
    /// </summary>
    Response,

    /// <summary>
    /// t = e
    /// </summary>
    Error
}

public struct QueryPackage
{
    /// <summary>
    /// q
    /// </summary>
    public string Method { get; set; }

    /// <summary>
    /// a
    /// </summary>
    public IDictionary<string, object> Arguments { get; set; }
}