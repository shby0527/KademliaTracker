using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Protocol;

namespace Umi.Dht.Client.Workers;

[Service(ServiceScope.Singleton)]
public class DistributedHashTableWorker(
    ILogger<DistributedHashTableWorker> logger,
    IKademliaNodeInstance instance) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogTrace("begin DHT tracking");
        instance.Start();
        return Task.CompletedTask;
    }


    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogTrace("Host Stopping");
        instance.Stop();
        return base.StopAsync(cancellationToken);
    }
}