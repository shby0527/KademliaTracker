using System.Buffers.Binary;
using System.Text;

namespace Umi.Dht.Control.Protocol.Pack;

public readonly struct AuthResponse
{
    public required int Result { get; init; }

    public required string Error { get; init; }

    public bool IsSuccess()
    {
        return (Result & 0x80_00_00_00) == 0;
    }

    public int ErrorCode()
    {
        return Result & 0x7F_FF_FF_FF;
    }

    public ReadOnlySpan<byte> Encode(Encoding encoding)
    {
        var errorBytes = encoding.GetBytes(Error);
        Span<byte> buffer = new byte[8 + errorBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], Result);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), errorBytes.Length);
        errorBytes.CopyTo(buffer[8..]);
        return buffer;
    }

    public static AuthResponse Decode(ReadOnlySpan<byte> data, Encoding encoding)
    {
        if (data.Length < 8) throw new FormatException("data format error");
        var result = BinaryPrimitives.ReadInt32LittleEndian(data[..4]);
        var errorLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        if (data[8..].Length < errorLength) throw new FormatException("data format error");
        var error = encoding.GetString(data[8..]);
        return new AuthResponse
        {
            Result = result,
            Error = error,
        };
    }

    public static int GetErrorCode(bool isSuccess, int code)
    {
        if (code < 0) throw new ArgumentException($"invalid argument value: {nameof(code)} must less than 0");
        return isSuccess ? code : (code | -2147483648);
    }
}