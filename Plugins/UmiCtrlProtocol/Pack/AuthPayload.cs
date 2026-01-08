using System.Buffers.Binary;
using System.Text;

namespace Umi.Dht.Control.Protocol.Pack;

public readonly struct AuthPayload
{
    public required string UserName { get; init; }


    public required string Password { get; init; }

    public ReadOnlyMemory<byte> Encode(Encoding encoding)
    {
        var usernameBytes = encoding.GetBytes(UserName);
        var passwdBytes = encoding.GetBytes(Password);
        Memory<byte> buffer = new byte[2 + usernameBytes.Length + 2 + passwdBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[..2].Span, (ushort)usernameBytes.Length);
        // write username bytes
        usernameBytes.CopyTo(buffer.Slice(2, usernameBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(2 + usernameBytes.Length, 2).Span,
            (ushort)passwdBytes.Length);
        passwdBytes.CopyTo(buffer[(2 + usernameBytes.Length + 2)..]);
        return buffer;
    }

    public static AuthPayload Decode(ReadOnlyMemory<byte> data, Encoding encoding)
    {
        if (data.Length < 2) throw new FormatException("package decode error");
        var userNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[..2].Span);
        if (data[2..].Length < userNameLength) throw new FormatException("username length error");
        var userName = encoding.GetString(data.Slice(2, userNameLength).Span);
        if (data[(2 + userNameLength)..].Length < 2) throw new FormatException("password length error");
        var passwordLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2 + userNameLength, 2).Span);
        if (data[(2 + userNameLength + 2)..].Length < passwordLength)
            throw new FormatException("password decode error");
        var password = encoding.GetString(data.Slice(2 + userNameLength + 2, passwordLength).Span);
        return new AuthPayload
        {
            UserName = userName,
            Password = password,
        };
    }
}