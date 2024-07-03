namespace Umi.Dht.Client.Operator;

public class CommandContext(string command, IEnumerable<string> arguments)
{
    private Dictionary<string, string>? _arguments;

    public string Command => command;

    public Dictionary<string, string> Arguments
    {
        get
        {
            if (_arguments != null) return _arguments;
            _arguments = new Dictionary<string, string>();
            foreach (var item in arguments)
            {
                var eqIdx = item.IndexOf('=');
                if (eqIdx < 0)
                {
                    _arguments[item] = string.Empty;
                    continue;
                }

                var key = item[..eqIdx];
                var value = item[(eqIdx + 1)..];
                _arguments[key] = value;
            }

            return _arguments;
        }
    }
}