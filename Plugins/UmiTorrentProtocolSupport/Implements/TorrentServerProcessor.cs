using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Control.Protocol.Pack;

namespace Umi.Dht.Torrent.Protocol.Implements;

public sealed class TorrentServerProcessor : IDisposable, IAsyncDisposable
{
    private readonly ILogger<TorrentServerProcessor> _logger;

    private readonly Socket _socket;

    private readonly SocketAsyncEventArgs _receiveEventArgs;

    private readonly IServiceScope _scope;

    private readonly Pipe _pipe = new();

    private readonly string _id;

    public event EventHandler? Close;

    public TorrentServerProcessor(IServiceScope scope, Socket acceptSocket)
    {
        _scope = scope;
        _socket = acceptSocket;
        _logger = scope.ServiceProvider.GetRequiredService<ILogger<TorrentServerProcessor>>();
        _receiveEventArgs = new SocketAsyncEventArgs();
        _receiveEventArgs.SetBuffer(new byte[4096]);
        _receiveEventArgs.Completed += this.OnReceive;
        _id = Guid.NewGuid().ToString();
    }

    public void Start()
    {
        Thread t = new(this.Process)
        {
            Name = $"client-{_id}",
            IsBackground = true
        };
        t.Start();
        this.BeginReceived();
    }

    private void Process()
    {
        using var tokenSource = new CancellationTokenSource();
        try
        {
            while (!this.ProcessPackage(tokenSource.Token).ConfigureAwait(false).GetAwaiter().GetResult()) ;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "process error, close");
        }

        _logger.LogInformation("process finished, stopping client");
        _pipe.Reader.Complete();
        this.StopAsync()
            .ContinueWith(_ => _logger.LogWarning("client stop"), tokenSource.Token);
    }


    private async Task<bool> ProcessPackage(CancellationToken token)
    {
        var reader = _pipe.Reader;
        var size = Marshal.SizeOf<BasePack>();
        var result = await reader.ReadAtLeastAsync(size, token);
        if (result is { IsCanceled: true } or { IsCompleted: true })
        {
            _logger.LogInformation("client package finished, stopping client");
            return true;
        }

        Span<byte> buffer = stackalloc byte[size];
        result.Buffer.Slice(0, size).CopyTo(buffer);
        BasePack.Decode(buffer, out var pack);
        
        return false;
    }

    private void BeginReceived()
    {
        if (!_socket.ReceiveAsync(_receiveEventArgs))
        {
            ThreadPool.QueueUserWorkItem(_ => this.OnReceive(_socket, _receiveEventArgs));
        }
    }

    public Task StopAsync()
    {
        _socket.Shutdown(SocketShutdown.Both);
        _socket.Close();
        if (Close is not null)
        {
            this.Close.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }


    private void OnReceive(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            _logger.LogTrace("remote closed");
            this.StopAsync()
                .ContinueWith(_ => _logger.LogDebug("client closed"));
            return;
        }

        _logger.LogTrace("received data, and write to buffer");
        var writer = _pipe.Writer;
        var buffer = writer.GetMemory(args.BytesTransferred);
        var memory = args.MemoryBuffer[..args.BytesTransferred];
        memory.CopyTo(buffer);
        writer.Advance(args.BytesTransferred);
        var task = writer.FlushAsync().AsTask();
        task.ContinueWith(t => _logger.LogTrace("flushed"));
        this.BeginReceived();
    }

    public void Dispose()
    {
        _socket.Dispose();
        _receiveEventArgs.Dispose();
        _scope.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await CastAndDispose(_socket);
        await CastAndDispose(_receiveEventArgs);
        await CastAndDispose(_scope);

        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
                await resourceAsyncDisposable.DisposeAsync();
            else
                resource.Dispose();
        }
    }
}