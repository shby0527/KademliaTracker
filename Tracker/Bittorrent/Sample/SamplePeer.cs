using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Bittorrent.Sample;

public sealed class SamplePeer : IBittorrentPeer, IDisposable
{
    public NodeInfo Node { get; }
    public IPAddress Address { get; }
    public int Port { get; }
    public bool IsConnected { get; }

    public void Connect()
    {
        throw new NotImplementedException();
    }

    public void Disconnect()
    {
        throw new NotImplementedException();
    }

    public ReadOnlySpan<byte> GetHashMetadata(long piece)
    {
        throw new NotImplementedException();
    }

    public bool Equals(IPeer? other)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}