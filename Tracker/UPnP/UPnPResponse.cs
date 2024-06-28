using System.Collections.Specialized;
using System.Net;

namespace Umi.Dht.Client.UPnP;

public class UPnPResponse
{
    public Version Version { get; private set; } = HttpVersion.Version11;

    public HttpStatusCode Code { get; private set; }

    public NameValueCollection Headers { get; private set; } = [];

    public static bool TryParse(string result, out UPnPResponse? response)
    {
        response = null;
        using var reader = new StringReader(result);
        var firstLine = reader.ReadLine();
        if (string.IsNullOrEmpty(firstLine)) return false;
        if (!firstLine.StartsWith("HTTP")) return false;
        var h = firstLine.Split();
        if (h.Length < 3) return false;
        response = new UPnPResponse();
        response.Version = Version.Parse(h[0].Replace("HTTP/", ""));
        response.Code = (HttpStatusCode)int.Parse((string)h[1]);
        string? next = null;
        while ((next = reader.ReadLine()) != null)
        {
            if (string.IsNullOrEmpty(next)) continue;
            var split = next.IndexOf(':');
            if (split < 0) continue;
            var name = next[..split].Trim();
            var value = next[(split + 1)..].Trim();
            response.Headers.Add(name, value);
        }

        return true;
    }
}