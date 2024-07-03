using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Operator;

namespace Umi.Dht.Client.Workers;

[Service(ServiceScope.Singleton)]
public class TelnetCommandWorker(
    ILogger<TelnetCommandWorker> logger,
    IServiceProvider provider,
    IConfiguration configuration
) : BackgroundService
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    private readonly SocketAsyncEventArgs _acceptArgs = new();

    private readonly ConcurrentBag<TelnetClient> _clients = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _acceptArgs.Completed += this.OnAccept;
        return Task.CompletedTask;
    }

    private void BeginAccept()
    {
    }

    private void OnAccept(object? sender, SocketAsyncEventArgs args)
    {
    }

    private void OnClientClose(object? sender, EventArgs eventArgs)
    {
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var client in _clients)
        {
            client.ClientClose -= this.OnClientClose;
            client.Close();
        }

        _acceptArgs.Completed -= this.OnAccept;
        _acceptArgs.Dispose();
        _socket.Close();
        _socket.Dispose();
        return base.StopAsync(cancellationToken);
    }
}