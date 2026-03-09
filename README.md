# CodeCompress

![CI](https://github.com/MCrank/code-compress/actions/workflows/ci.yml/badge.svg)

**Intelligent Code Index MCP Server for AI Agents**

CodeCompress is a lightweight [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that indexes your codebase and gives AI agents instant, surgical access to code symbols — no more scanning every file at the start of each conversation.

## The Problem

Every time an AI coding agent (Claude Code, Cursor, etc.) starts a new session, it has zero memory of your codebase. It compensates by reading through your source files to build understanding before doing any real work. As your project grows, this gets painful:

- **30–150k+ tokens wasted** per session just loading context
- **2–5 minutes of latency** before the agent starts being productive
- **Sub-agents duplicate the work**, each independently scanning the same files
- **Scales linearly** — bigger codebase = longer wait, every single time

## The Solution

CodeCompress maintains a persistent index of your codebase — symbols, types, dependencies, API surfaces — in a local SQLite database. AI agents query the index via MCP tools to get exactly what they need:

- `project_outline` returns your entire API surface in ~3–8k tokens (regardless of codebase size)
- `get_symbol` retrieves a single function's source code by byte offset
- `search_symbols` finds symbols by name, signature, or documentation
- `changes_since` shows what changed since a named snapshot

**Result: 80–90% reduction in token consumption for context loading.**

## Supported Languages

| Language | Status |
|----------|--------|
| Luau (Roblox) | Phase 1 — MVP |
| C# / .NET | Phase 2 |
| Python, TypeScript, Go, Rust | Planned |

Adding a new language requires implementing a single `ILanguageParser` interface — no changes to storage, indexing, or MCP tools.

## Installation

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### From Source

```bash
git clone https://github.com/MCrank/code-compress.git
cd code-compress
dotnet build CodeCompress.slnx
```

### As a Global Tool (coming soon)

```bash
dotnet tool install -g codecompress
```

## Setup

### Claude Code

Add to your Claude Code settings (`.claude/settings.json` or project-level):

```json
{
  "mcpServers": {
    "codecompress": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/code-compress/src/CodeCompress.Server"],
      "env": {}
    }
  }
}
```

If installed as a global tool:

```json
{
  "mcpServers": {
    "codecompress": {
      "command": "codecompress",
      "args": [],
      "env": {}
    }
  }
}
```

### Other MCP Clients

CodeCompress uses stdio transport by default. Any MCP-compatible client can connect by launching the server process and communicating over stdin/stdout.

## Usage

Once configured, AI agents can call these tools automatically. You can also use them directly via the optional CLI.

### Index Your Project

```
index_project(path: "/home/user/my-project")
```

First run does a full index. Subsequent runs are incremental — only re-parses files that changed (detected via SHA-256 hashing).

### Get a Project Overview

```
project_outline(path: "/home/user/my-project")
```

Returns a compressed summary of every module's public API surface — function signatures, types, and dependencies — in a fraction of the tokens it would take to read the files directly.

### Look Up a Specific Symbol

```
get_symbol(path: "/home/user/my-project", symbol_name: "CombatService:ProcessAttack")
```

Retrieves the full source code of a single function using byte-offset seeking. Reads only the exact bytes needed.

### Track Changes Between Sessions

```
snapshot_create(path: "/home/user/my-project", label: "before-refactor")
# ... make changes ...
changes_since(path: "/home/user/my-project", snapshot_label: "before-refactor")
```

Shows exactly what files and symbols were added, modified, or removed since the snapshot.

### Search Across Your Codebase

```
search_symbols(path: "/home/user/my-project", query: "damage OR health", kind: "function")
search_text(path: "/home/user/my-project", query: "TODO", glob: "*.luau")
```

Full-text search powered by SQLite FTS5.

### Explore Dependencies

```
dependency_graph(path: "/home/user/my-project", root_file: "src/server/Services/CombatService.luau")
```

Shows who requires/imports a file and what it depends on.

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `index_project` | Index a project directory (incremental by default) |
| `snapshot_create` | Create a named snapshot for delta tracking |
| `invalidate_cache` | Force full re-index on next run |
| `project_outline` | Compressed API surface of the entire project |
| `get_symbol` | Retrieve full source of a single symbol |
| `get_symbols` | Batch retrieve multiple symbols |
| `get_module_api` | Complete public API of a single module |
| `search_symbols` | Full-text search across symbol names and signatures |
| `search_text` | Raw text search across file contents |
| `changes_since` | Delta report against a named snapshot |
| `file_tree` | Annotated project file tree |
| `dependency_graph` | Import/require dependency graph |

## How It Works

```
AI Agent  ←— MCP Protocol (stdio) —→  CodeCompress Server
                                            │
                                       Tool Router
                                            │
                                       Index Engine
                                        ├── Language Parsers (Luau, C#, ...)
                                        └── SQLite Store (~/.codecompress/)
```

1. **Indexing** — CodeCompress walks your source files, hashes each one (SHA-256), and runs the appropriate language parser to extract symbols (functions, classes, types, constants) and dependencies (imports/requires).
2. **Storage** — Everything is stored in a local SQLite database at `~/.codecompress/`. Each project gets its own database file.
3. **Querying** — AI agents call MCP tools to get compressed outlines, look up specific symbols, search across the codebase, or check what changed since their last session.
4. **Incremental Updates** — On re-index, only files whose content hash changed are re-parsed. Unchanged files are skipped entirely.

## Security

CodeCompress is designed with security in mind:

- **Read-only** — never modifies your source files
- **Path traversal prevention** — all file paths are canonicalized and validated against the project root
- **SQL injection prevention** — all queries use parameterized statements
- **Prompt injection safeguards** — tool outputs are structured data; raw input is never echoed into freeform text
- **Local only** — no network calls, no telemetry, your code never leaves your machine

## License

MIT
