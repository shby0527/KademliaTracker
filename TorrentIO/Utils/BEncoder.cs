using System.Collections.Immutable;
using System.Text;

namespace Umi.Dht.Client.TorrentIO.Utils;

public static class BEncoder
{
    private static readonly byte I;

    private static readonly byte D;

    private static readonly byte L;

    private static readonly byte E;

    private static readonly byte SPM;

    static BEncoder()
    {
        var bytes = "idle:"u8.ToArray();
        I = bytes[0];
        D = bytes[1];
        L = bytes[2];
        E = bytes[3];
        SPM = bytes[4];
    }

    public static Dictionary<string, object> BDecodeToMap(ref ReadOnlySpan<byte>.Enumerator chars)
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

    public static List<object> BDecodeToList(ref ReadOnlySpan<byte>.Enumerator chars)
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
                result.Add(BDecodeToString(ref chars).ToArray());
            }
        }

        return result;
    }

    public static ReadOnlySpan<byte> BDecodeToString(ref ReadOnlySpan<byte>.Enumerator chars)
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

    public static int BDecodeToInteger(ref ReadOnlySpan<byte>.Enumerator chars)
    {
        if (chars.Current != I) throw new FormatException("can not convert to integer");
        StringBuilder number = new();
        while (chars.MoveNext() && chars.Current != E)
        {
            number.Append(Encoding.ASCII.GetString([chars.Current]));
        }

        if (!int.TryParse(number.ToString(), out var i))
        {
            throw new FormatException("can not convert to integer");
        }

        return i;
    }

    public static ReadOnlySpan<byte> BEncode(int number)
    {
        return Encoding.ASCII.GetBytes($"i{number}e");
    }

    public static ReadOnlySpan<byte> BEncode(ReadOnlySpan<byte> str)
    {
        List<byte> s = [];
        s.AddRange(Encoding.ASCII.GetBytes($"{str.Length}:"));
        s.AddRange(str);
        return s.ToArray();
    }

    public static ReadOnlySpan<byte> BEncode(IDictionary<string, object> dic)
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

    public static ReadOnlySpan<byte> BEncode(ICollection<object> list)
    {
        List<byte> sb = [L];
        foreach (var o in list)
        {
            sb.AddRange(BEncodeTypeMap(o));
        }

        sb.Add(E);
        return sb.ToArray();
    }

    public static ReadOnlySpan<byte> BEncodeTypeMap(object o)
    {
        return o switch
        {
            int value => BEncode(value),
            string str => BEncode(Encoding.ASCII.GetBytes(str)),
            ReadOnlyMemory<byte> b => BEncode(b.Span),
            byte[] s => BEncode(s),
            ICollection<object> list => BEncode(list),
            IDictionary<string, object> iDic => BEncode(iDic),
            _ => throw new ArgumentOutOfRangeException(nameof(o), "unknown type")
        };
    }
}