namespace Umi.Dht.Client.Commands;

public interface ICommandContext
{
}

public abstract class CommandResult
{
    public abstract ReadOnlySpan<byte> Encode();

    public static implicit operator string(CommandResult? result)
    {
        if (result is null) return "";
        return result.ToString() ?? "";
    }
}

public interface ICommand<in TContext, out TResult>
    where TContext : ICommandContext
    where TResult : CommandResult
{
    bool CanExecute(TContext context);

    TResult Execute(TContext context);
}