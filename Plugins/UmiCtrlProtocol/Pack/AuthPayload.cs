using System.Buffers.Binary;
using System.Text;

namespace Umi.Dht.Control.Protocol.Pack;

public readonly struct AuthPayload
{
    public ushort UserNameLength { get; init; }

    public required string UserName { get; init; }

    public ushort PasswordLength { get; init; }

    public required string Password { get; init; }

    public ReadOnlySpan<byte> Encode(Encoding encoding)
    {
        var usernameBytes = encoding.GetBytes(UserName);
        var passwdBytes = encoding.GetBytes(Password);
        Span<byte> buffer = new byte[2 + usernameBytes.Length + 2 + passwdBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[..2], (ushort)usernameBytes.Length);
        // write username bytes
        usernameBytes.CopyTo(buffer.Slice(2, usernameBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(2 + usernameBytes.Length, 2), (ushort)passwdBytes.Length);
        passwdBytes.CopyTo(buffer[(2 + usernameBytes.Length + 2)..]);
        return buffer;
    }

    public static AuthPayload Decode(ReadOnlySpan<byte> data, Encoding encoding)
    {
        if (data.Length < 2) throw new FormatException("package decode error");
        var userNameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
        if (data[2..].Length < userNameLength) throw new FormatException("username length error");
        var userName = encoding.GetString(data.Slice(2, userNameLength));
        if (data[(2 + userNameLength)..].Length < 2) throw new FormatException("password length error");
        var passwordLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2 + userNameLength, 2));
        if (data[(2 + userNameLength + 2)..].Length < passwordLength)
            throw new FormatException("password decode error");
        var password = encoding.GetString(data.Slice(2 + userNameLength + 2, passwordLength));
        return new AuthPayload
        {
            UserNameLength = userNameLength,
            UserName = userName,
            PasswordLength = passwordLength,
            Password = password,
        };
    }
}