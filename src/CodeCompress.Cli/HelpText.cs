using System.Globalization;
using System.Text;

namespace CodeCompress.Cli;

internal static class HelpText
{
    private sealed record CommandHelp(
        string Name,
        string Summary,
        string Description,
        IReadOnlyList<ParamHelp> Parameters,
        IReadOnlyList<string> Examples);

    private sealed record ParamHelp(string Name, string Description, bool Required, string? DefaultValue = null);

    private static readonly IReadOnlyList<CommandHelp> Commands =
    [
        new("index",
            "Index a project to build a searchable symbol database",
            "Scans the project directory, parses all supported source files, and stores symbols\n" +
            "in a SQLite database. Re-running performs an incremental update — only changed files\n" +
            "are re-parsed. Must be run before any query commands.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("language", "Filter to a specific language (e.g., luau, csharp)", Required: false),
            ],
            [
                "codecompress index --path C:\\Projects\\MyGame",
                "codecompress index --path /home/user/my-project --language luau",
            ]),

        new("outline",
            "Show a compressed project outline with symbol signatures",
            "Displays all indexed symbols grouped by file, kind, or directory.\n" +
            "Useful for getting a quick overview of a codebase structure.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
            ],
            [
                "codecompress outline --path C:\\Projects\\MyGame",
            ]),

        new("get-symbol",
            "Retrieve the full source code of a specific symbol",
            "Looks up a symbol by its qualified name and extracts its source code using\n" +
            "byte-offset seeking. Use 'Parent:Child' for nested symbols.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("name", "Qualified symbol name (e.g., CombatService:ProcessAttack)", Required: true),
            ],
            [
                "codecompress get-symbol --path C:\\Projects\\MyGame --name CombatService:ProcessAttack",
                "codecompress get-symbol --path /home/user/project --name globalFunction",
            ]),

        new("search",
            "Search the symbol index using full-text search",
            "Searches indexed symbol names using FTS5 syntax.\n" +
            "Supports AND, OR, NOT, quoted phrases, and prefix* matching.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("query", "FTS5 search query (e.g., Combat*, \"process attack\")", Required: true),
            ],
            [
                "codecompress search --path C:\\Projects\\MyGame --query Combat*",
                "codecompress search --path /home/user/project --query \"process AND attack\"",
            ]),

        new("search-text",
            "Search raw file contents using full-text search",
            "Searches the full text of indexed files using FTS5 syntax.\n" +
            "Use for string literals, comments, or patterns that are not symbol names.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("query", "FTS5 search query", Required: true),
            ],
            [
                "codecompress search-text --path C:\\Projects\\MyGame --query TODO",
                "codecompress search-text --path /home/user/project --query \"error handling\"",
            ]),

        new("changes",
            "Show what changed since a named snapshot",
            "Compares the current index state against a previously created snapshot.\n" +
            "Shows new, modified, and deleted files with symbol-level diffs.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("label", "Snapshot label to compare against", Required: true),
            ],
            [
                "codecompress changes --path C:\\Projects\\MyGame --label before-refactor",
            ]),

        new("snapshot",
            "Create a named snapshot of the current index state",
            "Saves the current file hashes and symbol state under a label.\n" +
            "Use before making changes, then run 'changes' to see what changed.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("label", "Human-readable label (auto-generated if omitted)", Required: false),
            ],
            [
                "codecompress snapshot --path C:\\Projects\\MyGame --label before-refactor",
                "codecompress snapshot --path /home/user/project",
            ]),

        new("file-tree",
            "Show an annotated directory tree with file and line counts",
            "Displays the project directory structure with file counts and total line counts\n" +
            "per directory. Does NOT require an index — reads the filesystem directly.",
            [
                new("path", "Absolute path to the directory to display", Required: true),
                new("depth", "Maximum directory depth (1-20)", Required: false, DefaultValue: "5"),
            ],
            [
                "codecompress file-tree --path C:\\Projects\\MyGame",
                "codecompress file-tree --path /home/user/project --depth 3",
            ]),

        new("deps",
            "Show the import/require dependency graph",
            "Displays which files depend on which, based on indexed import/require statements.\n" +
            "Can show the full project graph or start from a specific file.",
            [
                new("path", "Absolute path to the project root directory", Required: true),
                new("file", "Start from a specific file (relative path)", Required: false),
            ],
            [
                "codecompress deps --path C:\\Projects\\MyGame",
                "codecompress deps --path /home/user/project --file src/services/CombatService.luau",
            ]),
    ];

    private static readonly Dictionary<string, CommandHelp> CommandsByName =
        Commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownCommand(string name) =>
        CommandsByName.ContainsKey(name);

    public static string FormatGeneralHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CodeCompress CLI — Index and query code symbols");
        sb.AppendLine();
        sb.AppendLine("Usage: codecompress <command> [options]");
        sb.AppendLine();
        sb.AppendLine("Commands:");

        var maxLen = Commands.Max(c => c.Name.Length);
        foreach (var cmd in Commands)
        {
            sb.Append("  ");
            sb.Append(cmd.Name.PadRight(maxLen + 2));
            sb.AppendLine(cmd.Summary);
        }

        sb.AppendLine();
        sb.AppendLine("Run 'codecompress <command> --help' for details and examples.");
        return sb.ToString();
    }

    public static string FormatCommandHelp(string commandName)
    {
        if (!CommandsByName.TryGetValue(commandName, out var cmd))
        {
            return $"Unknown command: {commandName}\n\n{FormatGeneralHelp()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"codecompress {cmd.Name} — {cmd.Summary}");
        sb.AppendLine();
        sb.AppendLine(cmd.Description);
        sb.AppendLine();

        sb.AppendLine("Options:");
        var maxLen = cmd.Parameters.Max(p => p.Name.Length);
        foreach (var param in cmd.Parameters)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  --{param.Name.PadRight(maxLen + 2)}");
            if (param.Required)
            {
                sb.Append("(required) ");
            }

            sb.Append(param.Description);
            if (param.DefaultValue is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $" [default: {param.DefaultValue}]");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Examples:");
        foreach (var example in cmd.Examples)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {example}");
        }

        return sb.ToString();
    }
}
