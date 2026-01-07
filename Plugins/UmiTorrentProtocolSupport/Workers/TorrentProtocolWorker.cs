using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Torrent.Protocol.Configurations;
using Umi.Dht.Torrent.Protocol.Implements;

namespace Umi.Dht.Torrent.Protocol.Workers;

[Service(ServiceScope.Singleton)]
public sealed class TorrentProtocolWorker(
    ILogger<TorrentProtocolWorker> logger,
    IServiceProvider provider,
    IOptionsSnapshot<TorrentProtocolOptions> options)
    : BackgroundService
{
    private readonly TorrentProtocolOptions _options = options.Value;

    private readonly Socket _listenSocket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    private readonly SocketAsyncEventArgs _acceptSocketEventArgs = new();

    private readonly LinkedList<TorrentServerProcessor> _client = [];

    private readonly Lock _lock = new();


    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _acceptSocketEventArgs.SetBuffer(new byte[1024]);
        _acceptSocketEventArgs.Completed += this.OnAccept;
        _listenSocket.Bind(new IPEndPoint(IPAddress.Any, _options.Port));
        _listenSocket.Listen(10);
        this.BeginAccept();
        return Task.CompletedTask;
    }

    private void BeginAccept()
    {
        _acceptSocketEventArgs.AcceptSocket = null;
        if (!_listenSocket.AcceptAsync(_acceptSocketEventArgs))
        {
            ThreadPool.QueueUserWorkItem(_ => this.OnAccept(_listenSocket, _acceptSocketEventArgs));
        }
    }


    private void OnAccept(object? sender, SocketAsyncEventArgs e)
    {
        if (e is not { SocketError: SocketError.Success, AcceptSocket: not null })
        {
            logger.LogError("listen error, maybe close");
            return;
        }

        TorrentServerProcessor processor = new(provider.CreateScope(), e.AcceptSocket);
        processor.Close += this.OnClientClose;
        lock (_lock)
        {
            _client.AddLast(processor);
        }

        processor.Start();

        this.BeginAccept();
    }

    private void OnClientClose(object? sender, EventArgs e)
    {
        if (sender is null || sender is not TorrentServerProcessor processor) return;
        lock (_lock)
        {
            _client.Remove(processor);
        }

        processor.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _acceptSocketEventArgs.Dispose();
        _listenSocket.Dispose();

        // read only
        // ReSharper disable once InconsistentlySynchronizedField
        foreach (var processor in _client)
        {
            await processor.StopAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}