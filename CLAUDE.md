# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CodeCompress is an MCP (Model Context Protocol) server that indexes codebases and provides AI agents with compressed, surgical access to code symbols via SQLite-backed persistent indexes. It reduces AI agent token consumption by 80-90% when loading codebase context.

**Platform:** .NET 10 / C# 14 / Cross-platform
**License:** MIT

## Build & Test Commands

```bash
# Restore, build, and run all tests
dotnet build CodeCompress.slnx
dotnet test CodeCompress.slnx

# Run a specific test project
dotnet test tests/CodeCompress.Core.Tests
dotnet test tests/CodeCompress.Server.Tests
dotnet test tests/CodeCompress.Integration.Tests

# Run a single test by name
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# Run the MCP server (stdio transport)
dotnet run --project src/CodeCompress.Server

# Run the CLI tool
dotnet run --project src/CodeCompress.Cli
```

## Architecture

### Core Components

- **CodeCompress.Core** — Core library: parsers, indexing engine, SQLite storage, models, path validation
- **CodeCompress.Server** — MCP server executable with `[McpServerToolType]` tool classes
- **CodeCompress.Cli** — Optional standalone CLI for testing/debugging

### Key Patterns

- **Language Parsers** — Strategy pattern via `ILanguageParser`. Each parser declares its `LanguageId` and `FileExtensions`. The `IndexEngine` auto-resolves parsers by file extension via DI. Adding a new language = one class, no other changes.
- **Tool Router** — Attribute-based `[McpServerTool]` methods in tool classes under `Server/Tools/`
- **Symbol Store** — `ISymbolStore` / `SqliteSymbolStore` — repository pattern over `Microsoft.Data.Sqlite` with FTS5 virtual tables for full-text search
- **Index Engine** — `IIndexEngine` / `IndexEngine` — singleton service. Orchestrates file discovery, hashing, change detection, parsing, and storage updates via `Parallel.ForEachAsync`
- **File Hasher** — `IFileHasher` / `FileHasher` — parallel SHA-256 file hashing with `ArrayPool<byte>` buffering
- **Change Tracker** — `IChangeTracker` / `ChangeTracker` — pure-function diff of current vs stored hashes, produces `ChangeSet` (new/modified/deleted/unchanged)
- **Path Validation** — `PathValidator` (static) for path traversal prevention; `IPathValidator` / `PathValidatorService` wrapper for DI/testability
- **MCP Server Host** — `GenericHost` + `ModelContextProtocol` SDK, stdio transport
- **DI Registration** — `ServiceCollectionExtensions.AddCodeCompressCore()` registers all Core services

### Data Flow

```
AI Agent → MCP Protocol (stdio) → Tool Router → Index Engine → File Hasher (parallel SHA-256)
                                                             → Change Tracker (diff logic)
                                                             → Language Parsers (per extension)
                                                             → Symbol Store (SQLite)
```

### Database

SQLite stored at `.code-compress/index.db` in the project directory. Tables: `repositories`, `files`, `symbols`, `dependencies`, `index_snapshots`. FTS5 virtual tables: `symbols_fts`, `file_content_fts`.

### MCP Tools (4 categories)

1. **Indexing:** `index_project`, `snapshot_create`, `invalidate_cache`
2. **Query:** `project_outline`, `get_symbol`, `get_symbols`, `get_module_api`, `search_symbols`, `search_text`
3. **Delta:** `changes_since`, `file_tree`
4. **Dependency:** `dependency_graph`

## Development Methodology

### TDD is Mandatory

Every feature must be developed test-first using **TUnit** (source-generated, AOT-compatible):
1. Write failing test(s) defining expected behavior
2. Write minimum code to pass
3. Refactor while keeping tests green

**TUnit assertion style** — all assertions are async/fluent:
```csharp
await Assert.That(result.Symbols).HasCount(1);
await Assert.That(result.Symbols[0].Name).IsEqualTo("expected");
```

Use `[Arguments(...)]` attribute for parameterized tests. Use **NSubstitute** for mocking interfaces. Use **Verify** for snapshot testing complex outputs (outlines, dependency graphs).

**Test structure mirrors source:** every class in `CodeCompress.Core` has a corresponding test class in `CodeCompress.Core.Tests`.

### Coverage Targets

| Layer | Target |
|-------|--------|
| Parsers (Luau, C#) | 95%+ |
| Storage (SQLite) | 90%+ |
| Index Engine | 90%+ |
| MCP Tools | 85%+ |

## Security Requirements (OWASP Top 10)

MCP tool parameters (`path`, `query`) are **untrusted inputs** from AI agents.

- **Path traversal prevention (A01):** All file paths must be canonicalized via `Path.GetFullPath()` + starts-with check against project root. No `..` traversal. Reject paths outside project root. Implemented in `Validation/PathValidator.cs`.
- **SQL/FTS5 injection prevention (A03):** All SQL uses parameterized queries (`@param` syntax) — zero string concatenation. FTS5 queries must be sanitized.
- **Read-only access (A04):** No file modification tools. Only read source files.
- **Prompt injection prevention:** MCP tool outputs are consumed by AI agents. All tool responses must return only structured data — never echo back raw user-supplied input (file paths, search queries, snapshot labels) into freeform text fields without sanitization. Strip or escape any content that could be interpreted as agent instructions (e.g., markdown directives, system prompt fragments, or tool-call-like syntax embedded in file contents, symbol names, doc comments, or FTS5 results). Treat source file contents as untrusted — a malicious repo could contain symbols or comments designed to hijack the consuming agent's behavior.
- **No `dynamic` or `object` types** for user-facing data.
- **SonarAnalyzer.CSharp** enforces rules at build time. Zero warnings required.

## Build Configuration

- **Central Package Management:** All versions in `Directory.Packages.props` — individual `.csproj` files omit version numbers
- **Directory.Build.props:** Shared settings — `net10.0`, `LangVersion 14`, `Nullable enable`, `TreatWarningsAsErrors true`, `AnalysisLevel latest-all`, `EnforceCodeStyleInBuild true`
- **global.json:** Pins .NET SDK 10.0.100 with `rollForward: latestFeature`
- **SonarAnalyzer.CSharp:** Applied to all projects via `Directory.Build.props`

## Code Style

Enforced via `.editorconfig`:
- PascalCase for public members, `_camelCase` for private fields, `I` prefix for interfaces
- Allman brace style, 4-space indentation
- `var` when type is apparent, expression-bodied members for single-line
- `readonly` fields where possible

## Performance Conventions

- Async/await throughout — no blocking calls
- `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` for file parsing (zero-copy)
- `Parallel.ForEachAsync` for file hashing and parsing during full index
- SQLite: WAL mode, batch inserts via transactions, `PRAGMA synchronous=NORMAL`
- Prepared statements cached where possible

## Target Languages

**Phase 1 (MVP):** Luau (Roblox) — regex/pattern-based parser
**Phase 2:** C# / .NET — regex/pattern-based parser
**Future:** Python, TypeScript/JavaScript, Go, Rust

## Key NuGet Packages

| Package | Purpose |
|---------|---------|
| `ModelContextProtocol` | MCP SDK — server hosting, tool registration |
| `Microsoft.Data.Sqlite` | SQLite access with FTS5 |
| `Microsoft.Extensions.FileSystemGlobbing` | Glob pattern matching for file discovery |
| `Microsoft.Extensions.Hosting` | Generic host for DI, logging |
| `TUnit` | Testing framework |
| `NSubstitute` | Mocking |
| `Verify` | Snapshot testing |
| `SonarAnalyzer.CSharp` | Static analysis |
