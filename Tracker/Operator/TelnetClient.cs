using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Umi.Dht.Client.Operator;

public class TelnetClient(Socket socket, IServiceProvider provider)
{
    private readonly SocketAsyncEventArgs _eventArgs = new();

    public event EventHandler<EventArgs>? ClientClose;

    private Thread? _thread = null;

    private readonly Pipe _pipe = new();

    private readonly ILogger<TelnetClient> _logger = provider.GetRequiredService<ILogger<TelnetClient>>();

    public void Start()
    {
        Memory<byte> buffer = new byte[4096];
        _eventArgs.SetBuffer(buffer);
        _eventArgs.Completed += this.OnReceived;
        // start client
        if (socket.Connected)
        {
            _thread = new Thread(this.Process)
            {
                Name = "command reader"
            };
            _thread.Start();
            this.BeginReceived();
            socket.Send("Welcome Umi Kademlia Console\r\nCommand:>"u8.ToArray());
        }
        else
        {
            this.Close();
        }
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

    private void Process()
    {
        var reader = _pipe.Reader;
        using var stream = reader.AsStream();
        using var textReader = new StreamReader(stream, Encoding.UTF8);
        while (textReader.ReadLine() is { } line)
        {
            if (string.IsNullOrEmpty(line)) continue;
            _logger.LogTrace("received one line and process command {line}", line);
            var split = line.Split();
            try
            {
                var commandOperator = provider.GetRequiredService<ICommandOperator>();
                var ctx = split.Length <= 1
                    ? new CommandContext(split[0], [])
                    : new CommandContext(split[0], split[1..]);
                if (ctx.Command is "exit" or "bye")
                {
                    this.Close();
                    return;
                }

                var result = commandOperator.CommandExecute(ctx) + "\r\nCommand:>";
                // send result
                var sendBytes = socket.Send(Encoding.UTF8.GetBytes(result));
                _logger.LogTrace("send {bytes} bytes data", sendBytes);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "command execute error");
            }
        }
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