using System.Numerics;
using System.Text;
using Umi.Dht.Client.TorrentIO.Utils;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-RPC 协议格式 
/// </summary>
public struct KRpcPackage()
{
    public DateTimeOffset CreateTime { get; } = DateTimeOffset.Now;

    /// <summary>
    /// transaction Id ，事务ID，响应的相关请求的ID
    /// </summary>
    public required byte[] TransactionId { get; init; }

    public BigInteger FormattedTransaction => new(TransactionId, true, true);

    /// <summary>
    /// 响应类型, 每个类型对应下面任意一个字段
    /// </summary>
    public KRpcTypes Type { get; init; } = KRpcTypes.Query;

    /// <summary>
    /// t = q
    /// </summary>
    public QueryPackage? Query { get; set; } = null;

    /// <summary>
    /// r
    /// </summary>
    public IDictionary<string, object>? Response { get; set; } = null;

    /// <summary>
    /// e, list
    /// </summary>
    public (int Code, string Message)? Error { get; set; } = null;

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

        return BEncoder.BEncode(package);
    }

    public static KRpcPackage Decode(ReadOnlySpan<byte> buffer)
    {
        var result = buffer.GetEnumerator();
        // decode
        if (!result.MoveNext()) throw new FormatException("package format error");
        var dic = BEncoder.BDecodeToMap(ref result);
        // build package
        if (!dic.TryGetValue("t", out var transactionId))
        {
            throw new FormatException("package format error");
        }

        if (!dic.TryGetValue("y", out var type))
        {
            throw new FormatException("package format error");
        }

        KRpcPackage package = new()
        {
            TransactionId = (byte[])transactionId,
            Type = Encoding.ASCII.GetString((byte[])type)switch
            {
                "q" => KRpcTypes.Query,
                "r" => KRpcTypes.Response,
                "e" => KRpcTypes.Error,
                _ => throw new FormatException("package format error")
            }
        };
        switch (package.Type)
        {
            case KRpcTypes.Query:
                if (!dic.TryGetValue("q", out var method))
                {
                    throw new FormatException("package format error");
                }

                if (!dic.TryGetValue("a", out var arguments) || arguments is not IDictionary<string, object> ad)
                {
                    throw new FormatException("package format error");
                }

                package.Query = new QueryPackage
                {
                    Method = Encoding.ASCII.GetString((byte[])method) ?? "",
                    Arguments = ad
                };
                break;
            case KRpcTypes.Response:
                if (!dic.TryGetValue("r", out var response) || response is not IDictionary<string, object> r)
                {
                    throw new FormatException("package format error");
                }

                package.Response = r;
                break;
            case KRpcTypes.Error:
                if (!dic.TryGetValue("e", out var error) || error is not ICollection<object> e)
                {
                    throw new FormatException("package format error");
                }

                if (e.Count < 2)
                {
                    throw new FormatException("package format error");
                }

                var array = e.ToArray();
                package.Error = ((int)array[0], Encoding.ASCII.GetString((byte[])array[1]) ?? "");

                break;
            default:
                throw new ArgumentOutOfRangeException(null, "Type Unknown");
        }

        return package;
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