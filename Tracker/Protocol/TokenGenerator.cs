using System.Security.Cryptography;

namespace Umi.Dht.Client.Protocol;

public class TokenGenerator
{
    private readonly Memory<byte> _buffer = new byte[10];

    private DateTimeOffset _latestGen = DateTimeOffset.MinValue;

    private readonly TimeSpan _halfHour = TimeSpan.FromMinutes(30);

    private readonly Lock _sync = new();

    public ReadOnlySpan<byte> Token
    {
        get
        {
            if (DateTimeOffset.Now - _latestGen <= _halfHour) return _buffer.Span;
            lock (_sync)
            {
                if (DateTimeOffset.Now - _latestGen <= _halfHour) return _buffer.Span;
                _latestGen = DateTimeOffset.Now;
                RandomNumberGenerator.Fill(_buffer.Span);
            }

            return _buffer.Span;
        }
    }
}