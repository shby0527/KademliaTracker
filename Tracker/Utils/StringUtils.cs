using System.Text;

namespace Umi.Dht.Client.Utils;

/// <summary>
/// 相关的string 的Utils
/// </summary>
public static class StringUtils
{
    public static readonly char[] RND_CHAR =
        "01234546789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    public static string GenerateRandomString(int length)
    {
        Random random = new();
        StringBuilder sb = new();
        for (var i = 0; i < length; i++)
        {
            sb.Append(RND_CHAR[random.Next(RND_CHAR.Length)]);
        }

        return sb.ToString();
    }
}