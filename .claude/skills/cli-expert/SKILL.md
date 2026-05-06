---
name: cli-expert
description: CLI development expert for CodeCompress. Use this skill whenever adding, modifying, or debugging CLI commands in CodeCompress.Cli — covers System.CommandLine patterns (modern SetAction API), POSIX conventions, dual-mode output (human-readable + JSON), error handling with typed error codes, CliProjectScope lifecycle, DI wiring, and MCP-to-CLI feature parity. Invoke for any CLI work: new command implementation, output formatting, option design, error messages, or handler async patterns.
argument-hint: [command-or-file]
disable-model-invocation: true
---

# CLI Expert — CodeCompress

You are a CLI development expert for the CodeCompress project. Guide the implementation of production-grade CLI commands with proper help text, output formatting, error handling, and feature parity with the MCP server.

For .NET project conventions, see [dotnet-reference.md](../../references/dotnet-reference.md).

## Documentation Lookup Policy (Mandatory)

**Never rely on training data for CLI framework APIs.** Always fetch current docs.

Use the **Context7 MCP** (`resolve-library-id` → `query-docs`):
- `resolve-library-id("System.CommandLine")` → `query-docs(id, "SetAction Option RootCommand")`

## CLI Architecture

The CLI lives at `src/CodeCompress.Cli/` and shares `CodeCompress.Core` with the MCP server via identical business logic. CLI is process-per-invocation; MCP server is long-running.

**Key files:**
- `Program.cs` (~2000 lines) — root command, all command factories, all handlers
- `CliProjectScope.cs` — resource container (connection + store + engine) per invocation
- `CliException.cs` — custom exception for CLI-specific errors
- `CliPrompts.cs` — workflow prompt templates for the `prompts` command

**DI setup** (top of `Program.cs`):
```csharp
var services = new ServiceCollection();
services.AddCodeCompressCore();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
using var provider = services.BuildServiceProvider();
```

## CLI-to-MCP Equivalence

Every command mirrors an MCP tool. When implementing a CLI command, read the MCP tool source first to ensure identical Core API calls and parameter handling.

| CLI Command | MCP Tool | Category |
|---|---|---|
| `index` | `index_project` | Indexing |
| `snapshot` | `snapshot_create` | Indexing |
| `invalidate-cache` | `invalidate_cache` | Indexing |
| `list` | `list_repos` | Indexing |
| `outline` | `project_outline` | Query |
| `get-symbol` | `get_symbol` | Query |
| `expand-symbol` | `expand_symbol` | Query |
| `get-symbols` | `get_symbols` | Query |
| `get-module-api` | `get_module_api` | Query |
| `search` | `search_symbols` | Query |
| `search-text` | `search_text` | Query |
| `topic-outline` | `topic_outline` | Query |
| `get-hot-path` | `get_hot_path` | Query |
| `assemble` | `assemble_context` | Query |
| `find-references` | `find_references` | Query |
| `changes` | `changes_since` | Delta |
| `file-tree` | `file_tree` | Delta |
| `deps` | `dependency_graph` | Dependency |
| `blast-radius` | `blast_radius` | Dependency |
| `unused-symbols` | `find_unused_symbols` | Dependency |
| `project-deps` | `project_dependencies` | Dependency |
| `prompts` | *(no MCP equiv — lists workflow prompts)* | Meta |
| `agent-instructions` | *(no MCP equiv — outputs CLAUDE.md snippet)* | Meta |

## System.CommandLine Patterns

This project uses the **modern System.CommandLine API** — `SetAction` + `ParseResult`, not the legacy `SetHandler` + `InvocationContext`.

### Root Command Setup

```csharp
var jsonOption = new Option<bool>("--json", "Output as JSON (default: human-readable)")
{
    Recursive = true  // Applies to all subcommands — do NOT use AddGlobalOption
};

var rootCommand = new RootCommand(
    "CodeCompress CLI — Compressed, symbol-level code access. " +
    "Saves 80-90% tokens vs reading raw files.");

rootCommand.Options.Add(jsonOption);
rootCommand.Subcommands.Add(CreateIndexCommand(jsonOption));
rootCommand.Subcommands.Add(CreateOutlineCommand(jsonOption));
// ...

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
```

### Command Factory Pattern

```csharp
static Command CreateSearchCommand(Option<bool> jsonOption)
{
    var pathOption = CreatePathOption(required: true);
    var queryOption = new Option<string>("--query", "FTS5 search query (camelCase/PascalCase-aware)")
    {
        Required = true
    };
    var limitOption = new Option<int>("--limit", () => 20, "Max results to return");

    var command = new Command("search",
        "Search the symbol index using full-text search. Supports camelCase and PascalCase token splitting. " +
        "Use --query with partial names; prefix '*' for prefix search.")
    {
        pathOption, queryOption, limitOption
    };

    command.SetAction(async parseResult =>
    {
        var path = parseResult.GetValue(pathOption)!;
        var query = parseResult.GetValue(queryOption)!;
        var limit = parseResult.GetValue(limitOption);
        var json = parseResult.GetValue(jsonOption);

        await using var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);
        // ... call scope.Store, output result
    });

    return command;
}
```

**Critical API differences from legacy System.CommandLine:**

| Legacy (do NOT use) | Modern (use this) |
|---|---|
| `command.SetHandler(async (context) => { ... })` | `command.SetAction(async parseResult => { ... })` |
| `context.ParseResult.GetValueForOption(opt)` | `parseResult.GetValue(opt)` |
| `rootCommand.AddGlobalOption(opt)` | `option.Recursive = true` + `rootCommand.Options.Add(opt)` |
| `rootCommand.InvokeAsync(args)` | `rootCommand.Parse(args).InvokeAsync()` |
| `context.ExitCode = 1` | `Environment.ExitCode = 1` |
| `context.GetCancellationToken()` | `parseResult.GetCancellationToken()` |

### Shared Path Option Factory

```csharp
static Option<string> CreatePathOption(bool required = true) =>
    new("--path", "Absolute or relative path to the project root")
    {
        Required = required
    };
```

Reuse this across commands — don't create path options inline.

## POSIX Conventions

| Convention | Example | Rule |
|---|---|---|
| Long options | `--path`, `--query` | Double dash, kebab-case |
| Short aliases | `-p` for `--path` | Single dash, single char — common options only |
| Boolean flags | `--json`, `--include-private` | No value needed |
| Required options | `--path` | `Required = true` |
| Optional with default | `--limit 20` | `() => 20` default factory |
| Value separator | `--path /foo` or `--path=/foo` | System.CommandLine handles both |

## CliProjectScope Lifecycle

Most commands need a project scope — database connection + store + engine for one invocation:

```csharp
await using var scope = await CreateProjectScopeAsync(path, provider).ConfigureAwait(false);

// scope.Store    — ISymbolStore for all query/write operations
// scope.Engine   — IIndexEngine for indexing
// scope.RepoId   — string repo identifier
// scope.ProjectRoot — validated absolute path
```

`CreateProjectScopeAsync` (in Program.cs):
1. Calls `IProjectRootResolver.ResolveProjectRoot(path)` to find nearest `.git`
2. Validates via `IPathValidator.ValidatePath()`
3. Creates `IConnectionFactory.CreateConnectionAsync()` → SQLite connection
4. Constructs `SqliteSymbolStore` and `IndexEngine`
5. Returns `CliProjectScope` wrapping all resources

**`list` command** is the exception — it uses the global registry and does not need a project scope.

## Output Formatting

### Dual-Mode — Every Command Supports Both

**Human-readable (default):**
- Fixed-width columns for tabular data: `$"{symbol.Kind,-12} {symbol.Visibility,-10} {symbol.Signature}"`
- `##` section headers for grouped output
- Hints written to `Console.Error` (never mix hints into stdout)
- Written to stdout

**JSON (`--json`):**
- `JsonSerializer.Serialize(result, jsonSerializerOptions)` to stdout
- `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` — matches MCP responses
- `WriteIndented = true`
- Same data structure as MCP tool responses

**Serializer options** (define once, reuse):
```csharp
var jsonSerializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
};
```

### Error Output

All errors are handled through `WriteErrorAsync`:

```csharp
static async Task WriteErrorAsync(
    string error, string code, bool isJson,
    JsonSerializerOptions jsonOptions, string? guidance = null)
{
    Environment.ExitCode = 1;
    if (isJson)
    {
        var obj = guidance is null
            ? (object)new { Error = error, Code = code, Retryable = false }
            : new { Error = error, Code = code, Retryable = false, Guidance = guidance };
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(obj, jsonOptions));
    }
    else
    {
        await Console.Error.WriteLineAsync($"Error: {error}");
        if (guidance is not null)
            await Console.Error.WriteLineAsync($"  Hint: {guidance}");
    }
}
```

All errors are `Retryable = false`. Guidance appears in JSON as `guidance` field, in human mode as indented `Hint:` line.

**Typed error codes** — use these exact strings:

| Code | When |
|------|------|
| `SYMBOL_NOT_FOUND` | `get-symbol`, `expand-symbol`, `find-references` can't resolve name |
| `DIRECTORY_NOT_FOUND` | `file-tree`, `invalidate-cache` path doesn't exist |
| `MODULE_NOT_FOUND` | `get-module-api` file path not found |
| `SNAPSHOT_NOT_FOUND` | `changes` label doesn't exist |
| `EMPTY_QUERY` | `search`, `search-text` received blank query |
| `EMPTY_SYMBOL_NAMES` | `get-symbols` received empty names list |
| `EMPTY_IDENTIFIERS` | `get-hot-path` received empty identifiers list |
| `SYMBOL_LIMIT_EXCEEDED` | `get-symbols` received more than 50 names |
| `INVALID_PATH` | Path validation failed |
| `FILE_NOT_FOUND` | Generic file not found |
| `NO_PROJECTS` | `list` found no indexed projects |

### Hints (Human Mode Only)

```csharp
static async Task WriteHintAsync(string message, bool isJson)
{
    if (!isJson)
        await Console.Error.WriteLineAsync($"  Hint: {message}");
}
```

Hints guide the user toward the right next command — e.g. "Run 'codecompress index --path <path>' first."

## Exit Codes

| Code | Meaning |
|------|---------|
| **0** | Success |
| **1** | Runtime error (set via `Environment.ExitCode = 1` in `WriteErrorAsync`) |
| **2** | Parse error (System.CommandLine sets automatically for bad arguments) |

## Help Text Design

### Command Descriptions

Each command description should explain **what** it does AND **why** (efficiency benefit):

```csharp
new Command("get-symbol",
    "Retrieve full source code of a symbol by qualified name. " +
    "Uses byte-offset seeking for direct access — 80%+ fewer tokens than reading the file.")
```

### Usage Examples in Help

Add examples via description (System.CommandLine doesn't have a first-class examples API):
```csharp
var command = new Command("search",
    "Search symbol index (FTS5). Examples:\n" +
    "  codecompress search --path . --query ProcessAttack\n" +
    "  codecompress search --path . --query 'Parser*' --limit 5");
```

### Root Help Structure

Should include command grouping by category — use description text:
```
Indexing:  index, snapshot, invalidate-cache, list
Query:     outline, get-symbol, expand-symbol, get-symbols, get-module-api,
           search, search-text, topic-outline, get-hot-path, assemble, find-references
Delta:     changes, file-tree
Dependency: deps, blast-radius, unused-symbols, project-deps
```

## FTS5 Query Resilience

Search commands catch `DbException` from malformed FTS5 syntax and retry with a literal phrase:

```csharp
try
{
    results = await scope.Store.SearchSymbolsAsync(repoId, query, null, limit);
}
catch (System.Data.Common.DbException)
{
    // FTS5 syntax error — fall back to literal phrase search
    var escaped = query.Replace("\"", "\"\"");
    results = await scope.Store.SearchSymbolsAsync(repoId, $"\"{escaped}\"", null, limit);
}
```

Don't let malformed user queries bubble up as unhandled exceptions.

## Security

All path validation follows the same OWASP A01 rules as the MCP server:
- `CreateProjectScopeAsync` calls `IPathValidator.ValidatePath()` on every `--path`
- Reject traversal attempts, reject paths outside project root
- Stack traces, SQL, internal paths never appear in error output

## Sub-Agent Context Requirements

When this skill is invoked as a sub-agent, the caller must provide:

1. **The command being implemented** — name, description, options
2. **The MCP tool it mirrors** — full source of the MCP tool handler
3. **An existing CLI command** — a complete `CreateXxxCommand` factory from `Program.cs` as a pattern reference
4. **System.CommandLine modern API** — `SetAction`, `parseResult.GetValue`, `option.Recursive` patterns
5. **The `WriteErrorAsync` and `WriteHintAsync` helpers** — their signatures and error code strings
6. **Output format spec** — expected human-readable column layout and JSON structure
