using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umi.Dht.Control.Protocol;
using Umi.Dht.Control.Protocol.Pack;
using Umi.Dht.Torrent.Protocol.Configurations;

namespace Umi.Dht.Torrent.Protocol.Implements;

internal sealed class TorrentServerProcessor : IDisposable, IAsyncDisposable
{
    private readonly ILogger<TorrentServerProcessor> _logger;

    private readonly Socket _socket;

    private readonly SocketAsyncEventArgs _receiveEventArgs;

    private readonly IServiceScope _scope;

    private readonly Pipe _pipe = new();

    private readonly string _id;

    private readonly TorrentProtocolOptions _options;

    private byte[] _session = [];

    private bool _handshakeComplete;

    private bool _authed;

    private string _username;

    public event EventHandler? Close;

    public TorrentServerProcessor(IServiceScope scope, Socket acceptSocket)
    {
        _scope = scope;
        _socket = acceptSocket;
        _logger = scope.ServiceProvider.GetRequiredService<ILogger<TorrentServerProcessor>>();
        _options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<TorrentProtocolOptions>>().Value;
        _receiveEventArgs = new SocketAsyncEventArgs();
        _receiveEventArgs.SetBuffer(new byte[4096]);
        _receiveEventArgs.Completed += this.OnReceive;
        _id = Guid.NewGuid().ToString();
        _handshakeComplete = false;
        _authed = !_options.EnableAuthentication;
        _username = _options.EnableAuthentication ? string.Empty : "Guest";
    }

    public ReadOnlySpan<byte> Session => _handshakeComplete
        ? _session
        : throw new InvalidOperationException("Client Handshake not completed");

    public void Start()
    {
        if (!_socket.Connected)
        {
            _logger.LogWarning("client not connected");
            Close?.Invoke(this, EventArgs.Empty);
            return;
        }

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

        using var buffer = MemoryPool<byte>.Shared.Rent(size);
        result.Buffer.Slice(0, size).CopyTo(buffer.Memory.Span);
        reader.AdvanceTo(result.Buffer.GetPosition(size));
        BasePack.Decode(buffer.Memory, out var pack);
        // pack .. 
        if (!_handshakeComplete)
        {
            return await HandshakeProcess(pack, token);
        }

        if (!_authed)
        {
            if (pack.Command == Constants.AUTH)
            {
                return await ProcessAuthentication(pack, token);
            }

            // no authed
            return await SendTorrentResponse(Constants.AUTH_RESPONSE,
                TorrentResponse.GetErrorCode(false, Constants.REQUIRE_AUTH_ERROR_CODE),
                "server require authentication", token);
        }

        // common command execute

        return false;
    }


    private async Task<bool> ProcessAuthentication(BasePack pack, CancellationToken token)
    {
        // read payload
        var reader = _pipe.Reader;
        var result = await reader.ReadAtLeastAsync((int)pack.Length, token);
        using var buffer = MemoryPool<byte>.Shared.Rent((int)pack.Length);
        if (result is { IsCanceled: true } or { IsCompleted: true })
        {
            reader.AdvanceTo(result.Buffer.End);
            await reader.CompleteAsync();
            return true;
        }

        result.Buffer.Slice(0, (int)pack.Length).CopyTo(buffer.Memory.Span);
        var payload = AuthPayload.Decode(buffer.Memory, Encoding.UTF8);
        // all checked, not be null
        if (_options.Users!.TryGetValue(payload.UserName, out var pwd))
        {
            if (pwd == payload.Password)
            {
                _authed = true;
                _username = payload.UserName;
                return await SendTorrentResponse(Constants.AUTH_RESPONSE, 0, "Success", token);
            }
        }

        return await SendTorrentResponse(Constants.AUTH_RESPONSE,
            TorrentResponse.GetErrorCode(false, Constants.FAILURE_AUTH_ERROR_CODE),
            "Username or Password not matched", token);
    }

    private async Task<bool> SendTorrentResponse(byte cmd, int code, string msg, CancellationToken token)
    {
        TorrentResponse msgResponse = new()
        {
            Result = code,
            Error = msg
        };
        var msgBytes = msgResponse.Encode(Encoding.UTF8);
        var response = new BasePack
        {
            Magic = Constants.MAGIC,
            Command = cmd,
            Version = Constants.VERSION,
            Session = _session.Length == 20 ? _session : new byte[20],
            Length = (ulong)msgBytes.Length
        };
        var responseBytes = response.Encode();
        await _socket.SendAsync(responseBytes, token);
        await _socket.SendAsync(msgBytes, token);
        return false;
    }

    private async Task<bool> HandshakeProcess(BasePack pack, CancellationToken token)
    {
        // should handshake
        if (pack.Command != Constants.HANDSHAKE)
        {
            return await SendTorrentResponse(Constants.HANDSHAKE,
                TorrentResponse.GetErrorCode(false, Constants.HANDSHAKE_ERROR_CODE),
                "handshake required first", token);
        }

        _session = pack.Session;
        _handshakeComplete = true;
        return await SendTorrentResponse(Constants.HANDSHAKE, 0, "OK", token);
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
        this.Close?.Invoke(this, EventArgs.Empty);
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
        task.ContinueWith(_ => _logger.LogTrace("flushed"));
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