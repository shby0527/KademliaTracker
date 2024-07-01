using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Umi.Dht.Client.Protocol;

/// <summary>
/// K-RPC 协议格式 
/// </summary>
public struct KRpcPackage()
{
    private static readonly byte I;

    private static readonly byte D;

    private static readonly byte L;

    private static readonly byte E;

    private static readonly byte SPM;

    static KRpcPackage()
    {
        var bytes = "idle:"u8.ToArray();
        I = bytes[0];
        D = bytes[1];
        L = bytes[2];
        E = bytes[3];
        SPM = bytes[4];
    }


    public DateTimeOffset CreateTime { get; } = DateTimeOffset.Now;

    /// <summary>
    /// transaction Id ，事务ID，响应的相关请求的ID
    /// </summary>
    public required string TransactionId { get; init; }

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

        return BEncode(package);
    }

    public static KRpcPackage Decode(ReadOnlySpan<byte> buffer)
    {
        var result = buffer.GetEnumerator();
        // decode
        if (!result.MoveNext()) throw new FormatException("package format error");
        var dic = BDecodeToMap(ref result);
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
            TransactionId = Encoding.ASCII.GetString((byte[])transactionId),
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

    private static Dictionary<string, object> BDecodeToMap(ref ReadOnlySpan<byte>.Enumerator chars)
    {
        if (chars.Current != D) throw new FormatException("can not convert to map");
        var dictionary = new Dictionary<string, object>();
        while (chars.MoveNext() && chars.Current != E)
        {
            var key = Encoding.ASCII.GetString(BDecodeToString(ref chars));
            if (!chars.MoveNext())
            {
                dictionary[key] = "";
                break;
            }

            if (chars.Current == I)
            {
                dictionary[key] = BDecodeToInteger(ref chars);
            }
            else if (chars.Current == L)
            {
                dictionary[key] = BDecodeToList(ref chars);
            }
            else if (chars.Current == D)
            {
                dictionary[key] = BDecodeToMap(ref chars);
            }
            else
            {
                dictionary[key] = BDecodeToString(ref chars).ToArray();
            }
        }

        return dictionary;
    }

    private static List<object> BDecodeToList(ref ReadOnlySpan<byte>.Enumerator chars)
    {
        if (chars.Current != L) throw new FormatException("can not convert to list");
        List<object> result = [];
        while (chars.MoveNext() && chars.Current != E)
        {
            if (chars.Current == I)
            {
                result.Add(BDecodeToInteger(ref chars));
            }
            else if (chars.Current == L)
            {
                result.Add(BDecodeToList(ref chars));
            }
            else if (chars.Current == D)
            {
                result.Add(BDecodeToMap(ref chars));
            }
            else
            {
                result.Add(BDecodeToString(ref chars).ToString());
            }
        }

        return result;
    }

    private static ReadOnlySpan<byte> BDecodeToString(ref ReadOnlySpan<byte>.Enumerator chars)
    {
        StringBuilder lengthStr = new();
        do
        {
            lengthStr.Append(Encoding.ASCII.GetString([chars.Current]));
        } while (chars.MoveNext() && chars.Current != SPM);

        if (!int.TryParse(lengthStr.ToString(), out var length))
        {
            throw new FormatException("string format error");
        }

        List<byte> bytes = [];
        while (length > 0 && chars.MoveNext())
        {
            bytes.Add(chars.Current);
            length--;
        }

        if (length > 0) throw new FormatException("string length error");
        return bytes.ToArray();
    }

    private static int BDecodeToInteger(ref ReadOnlySpan<byte>.Enumerator chars)
    {
        if (chars.Current != I) throw new FormatException("can not convert to integer");
        StringBuilder number = new();
        while (chars.MoveNext() && chars.Current != E)
        {
            number.Append(Encoding.ASCII.GetString([chars.Current]));
        }

        if (int.TryParse(number.ToString(), out var i))
        {
            throw new FormatException("can not convert to integer");
        }

        return i;
    }

    private static ReadOnlySpan<byte> BEncode(int number)
    {
        return Encoding.ASCII.GetBytes($"i{number}e");
    }

    private static ReadOnlySpan<byte> BEncode(ReadOnlySpan<byte> str)
    {
        List<byte> s = [];
        s.AddRange(Encoding.ASCII.GetBytes($"{str.Length}:"));
        s.AddRange(str);
        return s.ToArray();
    }

    private static ReadOnlySpan<byte> BEncode(IDictionary<string, object> dic)
    {
        List<byte> sb = [D];
        foreach (var item in dic.ToImmutableSortedDictionary(StringComparer.Ordinal))
        {
            sb.AddRange(BEncode(Encoding.ASCII.GetBytes(item.Key)));
            sb.AddRange(BEncodeTypeMap(item.Value));
        }

        sb.Add(E);
        return sb.ToArray();
    }

    private static ReadOnlySpan<byte> BEncode(ICollection<object> list)
    {
        List<byte> sb = [L];
        foreach (var o in list)
        {
            sb.AddRange(BEncodeTypeMap(o));
        }

        sb.Add(E);
        return sb.ToArray();
    }

    private static ReadOnlySpan<byte> BEncodeTypeMap(object o)
    {
        return o switch
        {
            int value => BEncode(value),
            string str => BEncode(Encoding.ASCII.GetBytes(str)),
            ReadOnlyMemory<byte> b => BEncode(b.Span),
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