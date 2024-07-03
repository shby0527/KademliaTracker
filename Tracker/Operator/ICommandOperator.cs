namespace Umi.Dht.Client.Operator;

public interface ICommandOperator
{
    string CommandExecute(CommandContext ctx);
}