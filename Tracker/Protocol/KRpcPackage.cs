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
        using var result = Encoding.UTF8.GetString(buffer).GetEnumerator();
        // decode
        if (!result.MoveNext()) throw new FormatException("package format error");
        var dic = BDecodeToMap(result);
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
            TransactionId = transactionId.ToString() ?? "",
            Type = type.ToString() switch
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
                    Method = method.ToString() ?? "",
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
                package.Error = ((int)array[0], array[1].ToString() ?? "");

                break;
            default:
                throw new ArgumentOutOfRangeException(null, "Type Unknown");
        }

        return package;
    }

    private static Dictionary<string, object> BDecodeToMap(CharEnumerator chars)
    {
        if (chars.Current != 'd') throw new FormatException("can not convert to map");
        var dictionary = new Dictionary<string, object>();
        while (chars.MoveNext() && chars.Current != 'e')
        {
            var key = BDecodeToString(chars);
            if (!chars.MoveNext())
            {
                dictionary[key] = "";
                break;
            }

            dictionary[key] = chars.Current switch
            {
                'i' => BDecodeToInteger(chars),
                'l' => BDecodeToList(chars),
                'd' => BDecodeToMap(chars),
                _ => BDecodeToString(chars)
            };
        }

        return dictionary;
    }

    private static List<object> BDecodeToList(CharEnumerator chars)
    {
        if (chars.Current != 'l') throw new FormatException("can not convert to list");
        List<object> result = [];
        while (chars.MoveNext() && chars.Current != 'e')
        {
            result.Add(chars.Current switch
            {
                'i' => BDecodeToInteger(chars),
                'l' => BDecodeToList(chars),
                'd' => BDecodeToMap(chars),
                _ => BDecodeToString(chars)
            });
        }

        return result;
    }

    private static string BDecodeToString(CharEnumerator chars)
    {
        StringBuilder lengthStr = new();
        do
        {
            lengthStr.Append(chars.Current);
        } while (chars.MoveNext() && chars.Current != ':');

        if (!int.TryParse(lengthStr.ToString(), out var length))
        {
            throw new FormatException("string format error");
        }

        StringBuilder s = new();
        while (length > 0 && chars.MoveNext())
        {
            s.Append(chars.Current);
            length--;
        }

        if (length > 0) throw new FormatException("string length error");
        return s.ToString();
    }

    private static int BDecodeToInteger(CharEnumerator chars)
    {
        if (chars.Current != 'i') throw new FormatException("can not convert to integer");
        StringBuilder number = new();
        while (chars.MoveNext() && chars.Current != 'e')
        {
            number.Append(chars.Current);
        }

        if (int.TryParse(number.ToString(), out var i))
        {
            throw new FormatException("can not convert to integer");
        }

        return i;
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
            _ => throw new ArgumentOutOfRangeException(nameof(o), "unknown type")
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