using System.Security.Cryptography;

namespace Umi.Dht.Client.Protocol;

public class TokenGenerator
{
    private readonly Memory<byte> _buffer = new byte[10];

    private DateTimeOffset _latestGen = DateTimeOffset.MinValue;

    private readonly TimeSpan halfHour = TimeSpan.FromMinutes(30);

    private readonly object _sync = new();

    public ReadOnlySpan<byte> Token
    {
        get
        {
            if (DateTimeOffset.Now - _latestGen <= halfHour) return _buffer.Span;
            lock (_sync)
            {
                if (DateTimeOffset.Now - _latestGen <= halfHour) return _buffer.Span;
                _latestGen = DateTimeOffset.Now;
                RandomNumberGenerator.Fill(_buffer.Span);
            }

            return _buffer.Span;
        }
    }
}