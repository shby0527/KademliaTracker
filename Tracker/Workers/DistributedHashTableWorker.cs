using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Configurations;
using Umi.Dht.Client.Operator;
using Umi.Dht.Client.Protocol;
using Umi.Dht.Client.UPnP;

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
        _command.Add("avg", this.RouterNode);
        _command.Add("get_metadata", this.GetMetadata);
        _command.Add("rebootstrap", this.ReBootstrap);
        _command.Add("wanIP", this.GetWanIP);
        _command.Add("show_metadata", this.ShowMetadata);
    }

    private string ShowMetadata(IDictionary<string, string> arg)
    {
        return _kademliaNode?.ShowReceivedMetadata() ?? "not ready";
    }

    private string GetWanIP(IDictionary<string, string> dictionary)
    {
        var service = provider.GetRequiredService<IWanIPResolver>();
        if (service.ExternalIPAddress is null) return "";
        return service.ExternalIPAddress.Value;
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
               rebootstrap    retry bootstarp
               count          get type count
                              arguments:
                                    type=<node|kBucket> count of type
               listbtih       list BitTorrent Info Hash and peers counts
               avg            avg all count
               get_metadata   begin get all bt info hash metadata
                              arguments 
                                   btih=<value>   get this btih metadata
               show_metadata  show received metadata
               wanIP          get UPnP Wan IP Address
               """;
    }

    private string ListBitTorrentInfoHash(IDictionary<string, string> dictionary)
    {
        return _kademliaNode?.ListBitTorrentInfoHash() ?? "error execute command";
    }

    private string ReBootstrap(IDictionary<string, string> arguments)
    {
        _kademliaNode?.ReBootstrap();
        return "reBootstrapping";
    }

    private string RouterNode(IDictionary<string, string> arguments)
    {
        var routerAvg = _kademliaNode?.GetAvg();
        if (routerAvg == null) return "router not available";
        StringBuilder sb = new();
        sb.AppendLine($"Bucket Count {routerAvg.BucketCount}");
        foreach (var bh in routerAvg.Buckets)
        {
            sb.AppendLine($"\t\tBucket Distance {bh.Key[0]}-{bh.Key[1]}, Node Count {bh.Value}");
        }

        return sb.ToString();
    }

    private string GetMetadata(IDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue("btih", out var value))
        {
            _kademliaNode?.QueueReceiveInfoHashMetadata(value);
            return "Now, getting metadata";
        }

        return "no btih found";
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