using System.Text;
using Microsoft.Extensions.Logging;
using Umi.Dht.Client.Attributes;
using Umi.Dht.Client.Commands;
using Umi.Dht.Client.Operator;

namespace Umi.Dht.Client.Telnet.Implements;

[Service(ServiceScope.Singleton)]
public sealed class TelnetCommandInstance : ICommandOperator
{
    private readonly ILogger _logger;

    private readonly IKademliaCommand _command;

    private readonly IReadOnlyDictionary<string, Func<CommandContext, string>> _functions;

    public TelnetCommandInstance(ILogger<TelnetCommandInstance> logger, IKademliaCommand command)
    {
        _logger = logger;
        _command = command;
        _functions = new Dictionary<string, Func<CommandContext, string>>()
        {
            { "help", Help },
            { "rebootstrap", Rebootstrap },
            { "count", NodeBucketCount },
            { "list", ListObject },
        };
    }

    public string CommandExecute(CommandContext ctx)
    {
        _logger.LogTrace("Executing command '{0}'", ctx.Command);
        return _functions.TryGetValue(ctx.Command, out var function) ? function(ctx) : "Command not found";
    }

    private static string Help(CommandContext ctx)
    {
        return @"help: show this help message
rebootstrap: rejoin the DHT network
count type={node|bucket}: show node count or bucket count
list type={btih|torrent}: list torrent info hash";
    }

    private string Rebootstrap(CommandContext ctx)
    {
        _command.ReBootstrap();
        return "done, no errors";
    }

    private string NodeBucketCount(CommandContext ctx)
    {
        if (ctx.Arguments.Count < 1)
        {
            return "arguments not enough";
        }

        if (!ctx.Arguments.TryGetValue("type", out var type))
        {
            return "can not found argument 'type'";
        }

        return type switch
        {
            "node" => $"node count: {_command.GetNodeCount()}",
            "bucket" => $"k-bucket count: {_command.GetBucketCount()}",
            _ => "unknown type"
        };
    }

    private string ListObject(CommandContext ctx)
    {
        if (ctx.Arguments.Count < 1)
        {
            return "arguments not enough";
        }

        if (!ctx.Arguments.TryGetValue("type", out var type))
        {
            return "can not found argument 'type'";
        }

        return type switch
        {
            "btih" => this.ListBittorrentInfoHash(),
            "torrent" => this.ListReceivedTorrent(),
            _ => "unknown type"
        };
    }

    private string ListBittorrentInfoHash()
    {
        StringBuilder sb = new();

        var infoHash = _command.ListBitTorrentInfoHash();
        foreach (var item in infoHash)
        {
            sb.AppendLine($"{item.Key}: Peer Count: {item.Value.Count}, Torrent Received: {item.Value.Received}");
        }

        return sb.ToString();
    }

    private string ListReceivedTorrent()
    {
        StringBuilder sb = new();
        var metadata = _command.ShowReceivedMetadata();
        foreach (var info in metadata)
        {
            sb.AppendLine($"{info.Key}: \n\t{info.Value}");
        }

        return sb.ToString();
    }
}