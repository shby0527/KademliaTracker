using System.Collections.Concurrent;
using System.Net;
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

    private readonly List<TelnetClient> _clients = [];

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consolePort = configuration.GetValue<int?>("console-port") ?? 24489;
        _socket.Bind(new IPEndPoint(IPAddress.Any, consolePort));
        _socket.Listen(10);
        _acceptArgs.Completed += this.OnAccept;
        this.BeginAccept();
        return Task.CompletedTask;
    }

    private void BeginAccept()
    {
        _acceptArgs.AcceptSocket = null;
        if (!_socket.AcceptAsync(_acceptArgs))
        {
            ThreadPool.QueueUserWorkItem(_ => this.OnAccept(_socket, _acceptArgs));
        }
    }

    private void OnAccept(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, AcceptSocket: not null })
        {
            logger.LogError("listen error, maybe close");
            return;
        }

        TelnetClient client = new(args.AcceptSocket, provider);
        client.ClientClose += this.OnClientClose;
        _clients.Add(client);
        client.Start();

        this.BeginAccept();
    }

    private void OnClientClose(object? sender, EventArgs eventArgs)
    {
        if (sender is not TelnetClient client) return;
        try
        {
            client.ClientClose -= this.OnClientClose;
            lock (_clients)
            {
                _clients.Remove(client);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "closed");
        }
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