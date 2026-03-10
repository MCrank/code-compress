namespace CodeCompress.Cli;

internal sealed class CliArgs
{
    public string Command { get; }
    public bool HelpRequested { get; }

    private readonly Dictionary<string, string> _options;

    private CliArgs(string command, Dictionary<string, string> options, bool helpRequested)
    {
        Command = command;
        _options = options;
        HelpRequested = helpRequested;
    }

    public string? GetOption(string name) =>
        _options.TryGetValue(name, out var value) ? value : null;

    public string RequireOption(string name) =>
        GetOption(name) ?? throw new CliException($"Missing required option: --{name}");

    public static CliArgs Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliArgs(string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), helpRequested: true);
        }

        var command = string.Empty;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var helpRequested = false;

        var i = 0;

        // First non-option token is the command (store as-is, compare case-insensitively)
        if (args[0].Length > 0 && args[0][0] != '-')
        {
            command = args[0];
            i = 1;
        }

        while (i < args.Length)
        {
            var arg = args[i];

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                helpRequested = true;
                i++;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
            {
                var key = arg[2..];

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options[key] = args[i + 1];
                    i += 2;
                }
                else
                {
                    // Boolean flag (no value)
                    options[key] = "true";
                    i++;
                }
            }
            else
            {
                throw new CliException($"Unexpected argument: {arg}. Use --<option> <value> syntax.");
            }
        }

        return new CliArgs(command, options, helpRequested);
    }
}
