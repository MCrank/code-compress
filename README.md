![CodeCompress — MCP Server & CLI](https://raw.githubusercontent.com/MCrank/code-compress/develop/assets/banner.png)

**Persistent code index for AI agents. Ask for exactly what you need.**

[![CI](https://github.com/MCrank/code-compress/actions/workflows/ci.yml/badge.svg)](https://github.com/MCrank/code-compress/actions/workflows/ci.yml)
[![Release](https://github.com/MCrank/code-compress/actions/workflows/release.yml/badge.svg)](https://github.com/MCrank/code-compress/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/CodeCompress.Server?label=nuget)](https://www.nuget.org/packages/CodeCompress.Server)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CodeCompress.Server?label=downloads)](https://www.nuget.org/packages/CodeCompress.Server)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## What is CodeCompress?

CodeCompress is a **code intelligence tool** — available as both an [MCP server](https://modelcontextprotocol.io/) and a **standalone CLI** — that gives AI coding agents **instant memory of your codebase**. Use whichever interface fits your workflow: the MCP server integrates directly with AI tools like Claude Code, while the CLI works anywhere you have a terminal.

Instead of scanning every file at the start of each conversation, agents query a persistent SQLite index to get exactly the symbols, types, and dependencies they need — in a fraction of the tokens.

| Without CodeCompress | With CodeCompress |
|---|---|
| Agent reads 50+ files to understand your project | Agent calls `project_outline` — gets the full API surface in ~3–8k tokens |
| 30–150k+ tokens wasted per session on context | 80–90% reduction in context-loading tokens |
| Sub-agents each scan the same files independently | All agents share one persistent index |
| Bigger codebase = longer wait, every time | Index time is constant after first run (incremental) |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### Option A: MCP Server (recommended for AI tools)

Pick your tool and add the MCP server — that's it.

<details>
<summary><strong>Claude Code</strong></summary>

```bash
claude mcp add --transport stdio codecompress -- dnx CodeCompress.Server --yes
```

</details>

<details>
<summary><strong>VS Code / GitHub Copilot</strong></summary>

Create `.vscode/mcp.json` in your project:

```json
{
  "servers": {
    "codecompress": {
      "type": "stdio",
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

</details>

<details>
<summary><strong>Claude Desktop</strong></summary>

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "codecompress": {
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

</details>

<details>
<summary><strong>Cursor</strong></summary>

Create `.cursor/mcp.json` in your project:

```json
{
  "mcpServers": {
    "codecompress": {
      "type": "stdio",
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

</details>

<details>
<summary><strong>Windsurf</strong></summary>

Add to `~/.codeium/windsurf/mcp_config.json`:

```json
{
  "mcpServers": {
    "codecompress": {
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

</details>

<details>
<summary><strong>Other MCP clients</strong></summary>

CodeCompress uses stdio transport. Point your client at:

```
dnx CodeCompress.Server --yes
```

</details>

<details>
<summary><strong>Share with your team (.mcp.json)</strong></summary>

Commit this file at the root of your repo. Claude Code and VS Code pick it up automatically — every team member gets CodeCompress with zero setup:

```json
{
  "mcpServers": {
    "codecompress": {
      "type": "stdio",
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

</details>

### Option B: CLI (for terminals, scripts, and agents without MCP)

Install globally as a .NET tool:

```bash
dotnet tool install -g CodeCompress
```

Then use it from any terminal:

```bash
codecompress index --path /path/to/project
codecompress outline --path /path/to/project
codecompress search --path /path/to/project --query "MyClass"
```

The CLI and MCP server share the same index database — you can use both interchangeably. Run `codecompress --help` for all commands, or `codecompress agent-instructions` to generate a ready-to-paste instruction block for AI agents.

To update: `dotnet tool update -g CodeCompress`

### 2. Index your project

Your AI agent will call `index_project` automatically when it needs codebase context. You can also trigger it explicitly:

```
index_project(path: "/path/to/your/project")
```

First run does a **full index** — parses every source file and stores symbols in SQLite. Subsequent runs are **incremental** — only files whose SHA-256 hash changed get re-parsed.

### 3. Start coding

That's it. Your agent now has instant access to your codebase structure. It will automatically use tools like `project_outline`, `get_symbol`, and `search_symbols` instead of reading raw files.

## How It Works

```
AI Agent  <── MCP (stdio) ──>  CodeCompress Server ──┐
                                                      │
Developer <── Terminal ────>   CodeCompress CLI ──────┤
                                                      │
                                                 Index Engine
                                                   /      \
                                          Language       SQLite Store
                                          Parsers        .code-compress/
                                    (Luau, C#, Blazor,  index.db
                                   Terraform, JSON, …)
```

Both the MCP server and CLI share the same index database — you can index with one and query with the other.

1. **Index** — CodeCompress walks your source files, hashes each one (SHA-256), and extracts symbols (functions, classes, types, constants) and dependencies (imports/requires) using language-specific parsers.

2. **Store** — Everything goes into a local SQLite database at **`.code-compress/index.db`** in the project directory. The database uses FTS5 virtual tables for fast full-text search.

3. **Query** — Agents call MCP tools to get compressed outlines, look up specific symbols by name, search across the codebase, or check what changed since a snapshot.

4. **Stay current** — Re-indexing is incremental. Only files whose content hash changed are re-parsed. Call `index_project` at the start of any session, or after making changes, and it completes in seconds.

## Keeping the Index Up to Date

CodeCompress is designed to stay current with minimal effort:

| Scenario | What to do |
|---|---|
| **Starting a new agent session** | Call `index_project` — takes seconds if nothing changed |
| **After editing files** | Call `index_project` again — only changed files are re-parsed |
| **Tracking changes across sessions** | Use `snapshot_create` before work, then `changes_since` to see what's different |
| **Force a clean re-index** | Call `invalidate_cache` then `index_project` |

> **Tip for CLAUDE.md / system prompts:** Add an instruction like *"At the start of each session, call `index_project` to refresh the codebase index, then use `project_outline` to understand the project structure."* This ensures your agent always has a fresh index.

## Available MCP Tools

### Indexing

| Tool | What it does |
|------|---|
| `index_project` | Index a project directory (incremental by default) |
| `snapshot_create` | Create a named snapshot for tracking changes over time |
| `invalidate_cache` | Force a full re-index on next `index_project` call |

### Querying

| Tool | What it does |
|------|---|
| `project_outline` | Compressed API surface of the entire project — types, functions, signatures |
| `get_symbol` | Retrieve the full source code of a single symbol by name |
| `get_symbols` | Batch retrieve multiple symbols in one call |
| `get_module_api` | Complete public API of a single file/module |
| `search_symbols` | Full-text search across symbol names, signatures, and docs |
| `search_text` | Raw text search across file contents (with glob filtering) |

### Change Tracking & Navigation

| Tool | What it does |
|------|---|
| `changes_since` | Delta report — what files/symbols changed since a snapshot |
| `file_tree` | Annotated project file tree |
| `dependency_graph` | Import/require dependency graph for a file |

### Server Management

| Tool | What it does |
|------|---|
| `stop_server` | Gracefully shut down the server to release resources and DLL locks |

> **Note:** MCP clients like Claude Code automatically restart the server on the next tool call, so stopping it is always safe.

## Server Lifecycle

The `stop_server` tool provides on-demand shutdown — useful for releasing DLL/file locks during development without manually killing processes. The server exits gracefully, closing all SQLite connections, and restarts automatically on the next tool call.

## Supported Languages

| Language | Extensions | Status | Parser |
|----------|------------|--------|--------|
| Luau (Roblox) | `.luau`, `.lua` | Available | Regex/pattern-based |
| C# / .NET | `.cs` | Available | Regex/pattern-based |
| Blazor / Razor | `.razor` | Available | Directive extraction + C# delegation |
| Terraform / HCL | `.tf`, `.tfvars` | Available | Regex/pattern-based |
| Java | `.java` | Available | Regex/pattern-based |
| .NET Project Files | `.csproj`, `.fsproj`, `.props` | Available | XML-based |
| JSON Config | `.json` | Available | Structure-based |
| Python, TypeScript, Go, Rust | — | Planned | — |

Adding a new language requires implementing a single `ILanguageParser` interface — no changes to storage, indexing, or MCP tools.

## Where is my data stored?

All index data is stored **locally** in the project directory:

```
<project-root>/.code-compress/index.db
```

- One SQLite database per project, stored alongside the code
- Contains: file metadata, parsed symbols, dependencies, FTS5 search indexes, snapshots
- **No data leaves your machine** — no network calls, no telemetry
- Add `.code-compress/` to your `.gitignore` (WAL and SHM files should already be excluded)

To clear the index for a project, delete its `.code-compress/` directory.

## Security

- **Read-only** — never modifies your source files
- **Path traversal prevention** — all file paths canonicalized and validated against the project root
- **SQL injection prevention** — all queries use parameterized statements
- **Prompt injection safeguards** — tool outputs are structured data; raw input is never echoed into freeform text
- **Local only** — no network calls, no telemetry, your code stays on your machine

## Agent Configuration

Paste the following into your `CLAUDE.md`, `.cursorrules`, system prompt, or agent configuration file to teach AI agents how to use CodeCompress:

> **Tip:** You can also generate this block by running `codecompress agent-instructions` if you have the CLI installed.

````markdown
# CodeCompress — Agent Instructions

CodeCompress is a code intelligence tool that provides compressed, symbol-level access
to the indexed codebase. Use it as your PRIMARY tool for code discovery instead of reading
raw files — it saves 80-90% tokens.

## Workflow

1. **Index first** — `index_project` (MCP) or `codecompress index --path <root>` (CLI).
   Builds/updates the symbol database. Incremental — only changed files are re-parsed.
2. **Get an overview** — `project_outline` / `codecompress outline` for the full codebase structure.
3. **Search** — `search_symbols` / `codecompress search` for FTS5 full-text symbol search.
   `search_text` / `codecompress search-text` for raw file content search.
4. **Read symbols** — `get_symbol` / `codecompress get-symbol` to retrieve exact source code.
   `expand_symbol` / `codecompress expand-symbol` for a single method (~60% fewer tokens).
5. **Find references** — `find_references` / `codecompress find-references` to trace usage.
6. **Dependencies** — `dependency_graph` / `codecompress deps` for import relationships.

## Tips

- Add `--json` to any CLI command for machine-readable output (snake_case keys).
- The index persists at `<project-root>/.code-compress/index.db` — shared between MCP server and CLI.
- PREFER these tools over raw file reading. They are faster, more precise, and dramatically
  reduce token consumption.
````

## Building from Source

```bash
git clone https://github.com/MCrank/code-compress.git
cd code-compress
dotnet build CodeCompress.slnx
dotnet test --solution CodeCompress.slnx
```

To run the MCP server locally:

```bash
dotnet run --project src/CodeCompress.Server
```

To configure a client to use your local build:

```bash
claude mcp add --transport stdio codecompress -- dotnet run --project /absolute/path/to/src/CodeCompress.Server
```

## CLI Tool

The CLI provides the same capabilities as the MCP server — use whichever fits your workflow. Both share the same `.code-compress/index.db` database.

### Installation

```bash
dotnet tool install -g CodeCompress
```

To update to the latest version:

```bash
dotnet tool update -g CodeCompress
```

### Usage

```bash
# Index a project (must be run first)
codecompress index --path /path/to/project

# Get a compressed codebase overview
codecompress outline --path /path/to/project

# Search for symbols
codecompress search --path /path/to/project --query "Authentication*"

# Retrieve a specific symbol's source code
codecompress get-symbol --path /path/to/project --name MyClass:MyMethod

# Search raw file contents
codecompress search-text --path /path/to/project --query "TODO"

# Retrieve a nested method without loading the whole class (~60% token savings)
codecompress expand-symbol --path /path/to/project --name MyClass:MyMethod

# Batch retrieve multiple symbols at once
codecompress get-symbols --path /path/to/project --names "Foo,Bar,Baz"

# Get the public API surface of a single file
codecompress get-module-api --path /path/to/project --module src/Core/Foo.cs

# Search by topic — returns results in outline format
codecompress topic-outline --path /path/to/project --topic authentication

# Find all references to a symbol across the codebase
codecompress find-references --path /path/to/project --name ISymbolStore

# View directory structure (no index required)
codecompress file-tree --path /path/to/project

# Show file-level dependency graph
codecompress deps --path /path/to/project

# Show inter-project dependencies (.NET solutions)
codecompress project-deps --path /path/to/project

# Snapshot + change tracking
codecompress snapshot --path /path/to/project --label before-refactor
codecompress changes --path /path/to/project --label before-refactor

# Delete index to force full re-index
codecompress invalidate-cache --path /path/to/project
```

### JSON Output

Add `--json` to any command for machine-readable JSON output (snake_case keys matching MCP server output):

```bash
codecompress search --path /path/to/project --query "Parser" --json
```

### Agent Instructions

Generate a ready-to-paste instruction block for AI agents:

```bash
codecompress agent-instructions
```

This outputs a markdown block you can paste into `CLAUDE.md`, system prompts, or agent configuration files to teach AI agents how to use the CLI for code discovery.

### CLI-to-MCP Equivalence

| CLI Command | MCP Tool | Description |
|---|---|---|
| `index` | `index_project` | Build/update the symbol database |
| `outline` | `project_outline` | Compressed codebase overview |
| `get-symbol` | `get_symbol` | Retrieve symbol source code |
| `expand-symbol` | `expand_symbol` | Extract nested symbol (~60% fewer tokens) |
| `get-symbols` | `get_symbols` | Batch retrieve multiple symbols |
| `get-module-api` | `get_module_api` | Public API surface of a file |
| `search` | `search_symbols` | FTS5 symbol search |
| `search-text` | `search_text` | FTS5 raw content search |
| `topic-outline` | `topic_outline` | Topic-based search in outline format |
| `find-references` | `find_references` | Find all symbol references |
| `changes` | `changes_since` | Delta since snapshot |
| `snapshot` | `snapshot_create` | Create index snapshot |
| `file-tree` | `file_tree` | Directory tree |
| `deps` | `dependency_graph` | File-level dependency graph |
| `project-deps` | `project_dependencies` | Inter-project dependencies (.NET) |
| `invalidate-cache` | `invalidate_cache` | Force full re-index |

## License

[MIT](LICENSE)
