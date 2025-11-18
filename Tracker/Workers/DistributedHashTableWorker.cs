using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Configurations;
using Umi.Dht.Client.Operator;
using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Workers;

[Service(ServiceScope.Singleton)]
public class DistributedHashTableWorker(
    ILogger<DistributedHashTableWorker> logger,
    IOptions<KademliaConfig> kademliaConfig,
    IServiceProvider provider) : BackgroundService, ICommandOperator
{
    private KademliaNode? _kademliaNode;

    private readonly Dictionary<string, Func<IDictionary<string, string>, string>> _command = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogTrace("begin DHT tracking");
        this.InitialCommand();
        // init node id
        var generator = RandomNumberGenerator.Create();
        // 在当前文件夹内查找 缓存的node id
        var hostEnvironment = provider.GetRequiredService<IHostEnvironment>();
        var physicalPath = Path.Combine(hostEnvironment.ContentRootPath, ".nodeId");
        var fileInfo = new FileInfo(physicalPath);
        Memory<byte> nodeId = new byte[20];
        if (fileInfo.Exists)
        {
            using var nodeStream = fileInfo.OpenRead();
            nodeStream.ReadExactly(nodeId.Span);
        }
        else
        {
            generator.GetBytes(nodeId.Span);
            using var physicalStream = fileInfo.Create();
            physicalStream.Write(nodeId.Span);
            physicalStream.Flush();
        }

        logger.LogTrace("generate node Id {nodeId}", BitConverter.ToString(nodeId.ToArray()).Replace("-", ""));
        _kademliaNode = new KademliaNode(nodeId, provider, kademliaConfig.Value);
        _kademliaNode.Start();
        return Task.CompletedTask;
    }

    private void InitialCommand()
    {
        logger.LogTrace("now initial command handler");
        _command.Add("help", HelpCommand);
        _command.Add("get_peers", this.GetPeers);
        _command.Add("count", this.GetCount);
        _command.Add("listbtih", this.ListBitTorrentInfoHash);
    }


    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogTrace("Host Stopping");
        _kademliaNode?.Stop();
        return base.StopAsync(cancellationToken);
    }


    private static string HelpCommand(IDictionary<string, string> arguments)
    {
        return """
               Help List
               =====================
               get_peers      send get peers package
                              arguments:
                                    hash=<value>    sending parameters
               count          get type count
                              arguments:
                                    type=<node|kBucket> count of type
               listbtih       list BitTorrent Info Hash and peers counts
               """;
    }

    private string ListBitTorrentInfoHash(IDictionary<string, string> dictionary)
    {
        return _kademliaNode?.ListBitTorrentInfoHash() ?? "error execute command";
    }


    private string GetPeers(IDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("hash", out var value))
        {
            return "get_peers required argument hash";
        }

        if (value.Length != 40)
        {
            return "get_peers argument length error";
        }

        Span<byte> hash = stackalloc byte[20];
        try
        {
            for (var i = 0; i < value.Length; i += 2)
            {
                var bh = value[i..(i + 2)];
                hash[i / 2] = byte.Parse(bh, NumberStyles.HexNumber);
            }
        }
        catch (Exception e)
        {
            return e.Message;
        }

        _kademliaNode?.SendGetPeers(hash);
        return "package send";
    }

    private string GetCount(IDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue("type", out var v))
        {
            return v switch
            {
                "node" => _kademliaNode?.GetNodeCount() ?? "0",
                "kBucket" => _kademliaNode?.GetBucketCount() ?? "",
                _ => "unknown"
            };
        }

        return "unknown arguments";
    }

    public string CommandExecute(CommandContext ctx)
    {
        if (!_command.TryGetValue(ctx.Command, out var action))
            return $"can not found command {ctx.Command}, please try again.";
        return action(ctx.Arguments);
    }
}