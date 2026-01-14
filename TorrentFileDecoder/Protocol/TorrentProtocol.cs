using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Umi.Dht.Control.Protocol;
using Umi.Dht.Control.Protocol.Pack;

namespace TorrentFileDecoder.Protocol;

public sealed class TorrentProtocol : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    private readonly SocketAsyncEventArgs _receiveEventArgs = new();
    private readonly Pipe _pipe = new();

    private readonly IPEndPoint _endPoint;

    private readonly ReadOnlyMemory<byte> _session;

    private delegate void PackageHandler(in BasePack pack);

    private readonly IReadOnlyDictionary<byte, PackageHandler> _handlers;

    public event EventHandler<HandshakeCompleteEventArg>? HandshakeComplete;

    public event EventHandler? Closed;

    public event EventHandler<AuthenticationCompleteEventArg>? AuthenticationComplete;

    public bool HandshakeCompleted { get; private set; }

    public bool AuthenticationCompleted { get; private set; }

    public TorrentProtocol(IPAddress address, int port)
    {
        _endPoint = new IPEndPoint(address, port);
        _receiveEventArgs.SetBuffer(new byte[4096]);
        _socket.ReceiveBufferSize = 4096;
        _socket.SendBufferSize = 4096;
        HandshakeCompleted = false;
        _receiveEventArgs.Completed += this.OnReceive;
        _session = Utils.GenerateSession();
        _handlers = new Dictionary<byte, PackageHandler>()
        {
            { Constants.HANDSHAKE, HandshakeHandler },
            { Constants.AUTH_RESPONSE, AuthenticationResponse }
        }.ToImmutableDictionary();
    }


    private ReadOnlyMemory<byte> CreateBasePackData(byte command, ulong length)
    {
        BasePack pack = new()
        {
            Magic = Constants.MAGIC,
            Version = Constants.VERSION,
            Session = _session.ToArray(),
            Command = command,
            Length = length
        };
        return pack.Encode();
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        await _socket.ConnectAsync(_endPoint, token);
        // send handshake
        BasePack pack = new()
        {
            Magic = Constants.MAGIC,
            Version = Constants.VERSION,
            Command = Constants.HANDSHAKE,
            Session = _session.ToArray(),
            Length = 0
        };
        await _socket.SendAsync(pack.Encode(), token);
        if (!_socket.ReceiveAsync(_receiveEventArgs))
            ThreadPool.QueueUserWorkItem(_ => this.OnReceive(_socket, _receiveEventArgs));
        var p = new Thread(this.Process)
        {
            Name = "Background Process",
            IsBackground = true
        };
        p.Start();
    }

    private void Process()
    {
        var reader = _pipe.Reader;
        var size = Marshal.SizeOf<BasePack>();
        while (true)
        {
            var result = reader.ReadAtLeastAsync(size).AsTask()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (result is { IsCanceled: true } or { IsCompleted: true }) break;
            using var memory = MemoryPool<byte>.Shared.Rent(size);
            result.Buffer.Slice(result.Buffer.Start, size).CopyTo(memory.Memory.Span);
            reader.AdvanceTo(result.Buffer.GetPosition(size));
            if (!BasePack.Decode(memory.Memory, out var pack)) continue;
            if (_handlers.TryGetValue(pack.Command, out var handler))
            {
                handler(pack);
            }
        }

        reader.Complete();
    }

    public async Task SystemAuthenticateAsync(string username, string password, CancellationToken token = default)
    {
        AuthPayload payload = new()
        {
            UserName = username,
            Password = password
        };
        var payloadData = payload.Encode(Encoding.UTF8);
        var basePack = CreateBasePackData(Constants.AUTH, (ulong)payloadData.Length);
        await _socket.SendAsync(basePack, token);
        await _socket.SendAsync(payloadData, token);
    }


    private async Task<TorrentResponse> ReadTorrentResponse(ulong length)
    {
        var result = await _pipe.Reader.ReadAtLeastAsync((int)length);
        // only TorrentResponse
        using var memory = MemoryPool<byte>.Shared.Rent((int)length);
        result.Buffer.Slice(0, (int)length).CopyTo(memory.Memory.Span);
        _pipe.Reader.AdvanceTo(result.Buffer.GetPosition((long)length));
        var response = TorrentResponse.Decode(memory.Memory, Encoding.UTF8);
        return response;
    }

    private void HandshakeHandler(in BasePack pack)
    {
        // read handshake response
        this.ReadTorrentResponse(pack.Length)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;
                HandshakeCompleted = t.Result.IsSuccess();
                HandshakeComplete?.Invoke(this, new HandshakeCompleteEventArg(t.Result.IsSuccess(),
                    t.Result.ErrorCode() == Constants.REQUIRE_AUTH_ERROR_CODE,
                    t.Result.Error));
            });
    }

    private void AuthenticationResponse(in BasePack pack)
    {
        // read auth response
        this.ReadTorrentResponse(pack.Length)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;
                AuthenticationCompleted = t.Result.IsSuccess();
                AuthenticationComplete?.Invoke(this,
                    new AuthenticationCompleteEventArg(t.Result.IsSuccess(), t.Result.Error));
            });
    }

    private void OnReceive(object? sender, SocketAsyncEventArgs e)
    {
        if (e is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            Closed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var writer = _pipe.Writer;
        var memory = writer.GetMemory(e.BytesTransferred);
        e.MemoryBuffer[..e.BytesTransferred].Span.CopyTo(memory.Span);
        writer.Advance(e.BytesTransferred);
        writer.FlushAsync()
            .AsTask()
            .ContinueWith(_ =>
            {
                if (_socket.ReceiveAsync(_receiveEventArgs)) return;
                ThreadPool.QueueUserWorkItem(_ => this.OnReceive(_socket, _receiveEventArgs));
            });
    }


    public void Dispose()
    {
        if (_socket.Connected)
        {
            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();
        }

        _socket.Dispose();
        _receiveEventArgs.Dispose();
    }
}