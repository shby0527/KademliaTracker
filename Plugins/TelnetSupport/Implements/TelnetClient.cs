using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Operator;
using Umi.Dht.Client.Telnet.Configurations;

namespace Umi.Dht.Client.Telnet.Implements;

public class TelnetClient(Socket socket, IServiceProvider provider, TelnetOptions options)
{
    private readonly SocketAsyncEventArgs _eventArgs = new();

    public event EventHandler<EventArgs>? ClientClose;

    private Thread? _thread = null;

    private readonly Pipe _pipe = new();

    private readonly ILogger<TelnetClient> _logger = provider.GetRequiredService<ILogger<TelnetClient>>();

    private bool _auth = false;
    private string _username = "";

    private const byte IAC = 0xFF;

    private const byte WILL = 0xFC;

    private const byte WONT = 0xFB;

    private const byte DO = 0xFD;

    private const byte DONT = 0xFE;

    private const byte ECHO = 0x1;

    public void Start()
    {
        Memory<byte> buffer = new byte[4096];
        _eventArgs.SetBuffer(buffer);
        _eventArgs.Completed += this.OnReceived;
        // start client
        if (socket.Connected)
        {
            _thread = new Thread(() => this.Process().ConfigureAwait(false).GetAwaiter().GetResult())
            {
                Name = "command reader"
            };
            _thread.Start();
            this.BeginReceived();
            this.SendOption(WILL, ECHO);
            socket.Send("Welcome Umi Kademlia Console\r\nUsername:"u8.ToArray());
        }
        else
        {
            this.Close();
        }
    }

    private void SendOption(byte cmd, byte option)
    {
        Span<byte> buffer = stackalloc byte[3];
        buffer[0] = IAC;
        buffer[1] = cmd;
        buffer[2] = option;
        socket.Send(buffer);
    }

    private void OnReceived(object? sender, SocketAsyncEventArgs args)
    {
        if (args is not { SocketError: SocketError.Success, BytesTransferred: > 0 })
        {
            _logger.LogTrace("remote closed");
            this.Close();
            return;
        }

        _logger.LogTrace("received data, and write to buffer");
        var writer = _pipe.Writer;
        var buffer = writer.GetMemory(args.BytesTransferred);
        var memory = args.MemoryBuffer[..args.BytesTransferred];
        memory.CopyTo(buffer);
        writer.Advance(args.BytesTransferred);
        var task = writer.FlushAsync().AsTask();
        task.ConfigureAwait(false)
            .GetAwaiter()
            .OnCompleted(() => _logger.LogTrace("flushed"));
        this.BeginReceived();
    }

    private void BeginReceived()
    {
        if (!socket.ReceiveAsync(_eventArgs))
        {
            ThreadPool.QueueUserWorkItem(_ => this.OnReceived(socket, _eventArgs));
        }
    }

    private async Task Process()
    {
        var reader = _pipe.Reader;
        while (true)
        {
            var result = await reader.ReadAtLeastAsync(1);
            if (result.IsCanceled || result.IsCompleted) break;
            if (result.Buffer.FirstSpan[0] == IAC)
            {
                // 处理IAC 
                await ProcessIac(result);
                continue;
            }

            // 普通命令
            await ProcessNormal(result);
        }

        await reader.CompleteAsync();
    }

    private async Task<string> ReadLineText(ReadResult result, Encoding encoding)
    {
        var sequence = result.Buffer;
        var position = 0;
        byte latest = 0;
        while (true)
        {
            var enumerator = sequence.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var memory = enumerator.Current;
                // 寻找 /r/n
                for (var i = 0; i < memory.Length; i++)
                {
                    if (latest == '\r' && memory.Span[i] == '\n')
                    {
                        position++;
                        goto SEQ_EACH_END;
                    }

                    position++;
                    latest = memory.Span[i];
                }
            }

            _pipe.Reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            result = await _pipe.Reader.ReadAsync();
            sequence = result.Buffer;
        }

        SEQ_EACH_END:
        var data = new byte[position];
        sequence.Slice(sequence.Start, position).CopyTo(data);
        var end = sequence.GetPosition(position);
        _pipe.Reader.AdvanceTo(end);
        return encoding.GetString(data).TrimEnd('\r', '\n');
    }

    private async Task ProcessNormal(ReadResult result)
    {
        if (!_auth)
        {
            // 优先 认证逻辑
            var username = await ReadLineText(result, Encoding.UTF8);
            await socket.SendAsync("Password:"u8.ToArray());
            SendOption(WONT, ECHO);
            await ProcessIac(await _pipe.Reader.ReadAsync());
            var password = await ReadLineText(await _pipe.Reader.ReadAsync(), Encoding.UTF8);
            SendOption(WILL, ECHO);
            await ProcessIac(await _pipe.Reader.ReadAsync());
            if (options.Users.TryGetValue(username, out var pwd))
            {
                if (pwd == password)
                {
                    _auth = true;
                    _username = username;
                    await socket.SendAsync("\r\nKad:>"u8.ToArray());
                    return;
                }

                await socket.SendAsync("\r\nusername or password incorrect\r\nUsername:"u8.ToArray());
                return;
            }
        }

        var cmd = await ReadLineText(result, Encoding.UTF8);
        if (string.IsNullOrEmpty(cmd)) return;
        var split = cmd.Split();
        var commandOperator = provider.GetRequiredService<ICommandOperator>();
        var ctx = split.Length <= 1
            ? new CommandContext(split[0], [])
            : new CommandContext(split[0], split[1..]);
        if (split[0] is "bye" or "exit")
        {
            this.Close();
            return;
        }

        try
        {
            var execute = commandOperator.CommandExecute(ctx);
            await socket.SendAsync(Encoding.UTF8.GetBytes($"{execute}\r\nKad:>"));
        }
        catch (Exception)
        {
            await socket.SendAsync("error to execute command"u8.ToArray());
        }
    }


    private async Task ProcessIac(ReadResult result)
    {
        if (result.Buffer.Length < 3)
        {
            _pipe.Reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            result = await _pipe.Reader.ReadAtLeastAsync(3);
        }

        // IAC
        _logger.LogDebug("processing IAC cmd");
        var sequence = result.Buffer;
        Span<byte> buffer = stackalloc byte[3];
        sequence.Slice(sequence.Start, 3).CopyTo(buffer);
        _logger.LogDebug("IAC cmd,{op} {cmd}", buffer[1], buffer[2]);
        var position = sequence.GetPosition(3);
        _pipe.Reader.AdvanceTo(position);
    }


    public void Close()
    {
        if (socket.Connected)
        {
            socket.Disconnect(false);
        }

        _pipe.Writer.Complete();
        socket.Close();
        _eventArgs.Completed -= this.OnReceived;
        _eventArgs.Dispose();
        socket.Dispose();
        this.ClientClose?.Invoke(this, EventArgs.Empty);
    }
}