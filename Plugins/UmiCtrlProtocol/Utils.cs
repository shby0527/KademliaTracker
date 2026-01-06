using System.Security.Cryptography;

namespace Umi.Dht.Control.Protocol;

public static class Utils
{
    private static readonly RandomNumberGenerator Generator = RandomNumberGenerator.Create();

    public static ReadOnlyMemory<byte> GenerateSession()
    {
        var session = new byte[20];
        Generator.GetBytes(session);
        return session;
    }

    public static string FormatSession(ReadOnlySpan<byte> session)
    {
        return Convert.ToHexString(session);
    }
}