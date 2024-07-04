using System.Security.Cryptography;
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogTrace("begin DHT tracking");
        // init node id
        var generator = RandomNumberGenerator.Create();
        Memory<byte> nodeId = new byte[20];
        generator.GetBytes(nodeId.Span);
        logger.LogTrace("generate node Id {nodeId}", BitConverter.ToString(nodeId.ToArray()).Replace("-", ""));
        _kademliaNode = new KademliaNode(nodeId, provider, kademliaConfig.Value);
        _kademliaNode.Start();
        return Task.CompletedTask;
    }


    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogTrace("Host Stopping");
        _kademliaNode?.Stop();
        return base.StopAsync(cancellationToken);
    }

    public string CommandExecute(CommandContext ctx)
    {
        return "test";
    }
}