# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CodeCompress is an MCP (Model Context Protocol) server that indexes codebases and provides AI agents with compressed, surgical access to code symbols via a single global SQLite-backed index. It reduces AI agent token consumption by 80-90% when loading codebase context.

**Platform:** .NET 10 / C# 14 / Cross-platform
**License:** MIT

## Build & Test Commands

```bash
# Restore, build, and run all tests
dotnet build CodeCompress.slnx
dotnet test CodeCompress.slnx -- --output Normal --disable-logo

# Run a specific test project
dotnet test tests/CodeCompress.Core.Tests -- --output Normal --disable-logo
dotnet test tests/CodeCompress.Server.Tests -- --output Normal --disable-logo
dotnet test tests/CodeCompress.Integration.Tests -- --output Normal --disable-logo

# Filter by class name  (pattern: /{assembly}/{namespace}/{class}/{method})
dotnet test tests/CodeCompress.Core.Tests -- --output Normal --disable-logo --treenode-filter "/*/*/TestClassName/*"

# Filter by specific test method
dotnet test tests/CodeCompress.Core.Tests -- --output Normal --disable-logo --treenode-filter "/*/*/TestClassName/MethodName"

# Run the MCP server (stdio transport)
dotnet run --project src/CodeCompress.Server

# Run the CLI tool
dotnet run --project src/CodeCompress.Cli
```

> **TUnit flag syntax:** TUnit runs on Microsoft.Testing.Platform. Flags like `--output`, `--disable-logo`, and `--treenode-filter` are MTP/TUnit args — they must follow the `--` separator when using `dotnet test`. Do NOT use `--filter "FullyQualifiedName~..."` (legacy VSTest syntax — not supported).

## Architecture

### Core Components

- **CodeCompress.Core** — Core library: parsers, indexing engine, SQLite storage, models, path validation, registry
- **CodeCompress.Server** — MCP server executable with `[McpServerToolType]` tool classes and `[McpServerPromptType]` prompt classes
- **CodeCompress.Cli** — Optional standalone CLI for testing/debugging

### Key Patterns

- **Language Parsers** — Strategy pattern via `ILanguageParser`. Each parser declares its `LanguageId` and `FileExtensions`. The `IndexEngine` auto-resolves parsers by file extension via DI. Adding a new language = one class, no other changes.
- **Tool Router** — Attribute-based `[McpServerTool]` methods in tool classes under `Server/Tools/`
- **MCP Prompts** — `[McpServerPrompt]` methods in `Server/Prompts/PromptsProvider.cs` expose named workflows (e.g. `ExploreCodebase`, `FindImpact`) that guide agents through multi-tool sequences. This is the progressive tool disclosure mechanism — agents discover the right tool sequence via prompts rather than being exposed to all 22 tools at once.
- **Symbol Store** — `ISymbolStore` / `SqliteSymbolStore` — repository pattern over `Microsoft.Data.Sqlite` with FTS5 virtual tables for full-text search
- **Index Engine** — `IIndexEngine` / `IndexEngine` — singleton service. Orchestrates file discovery, hashing, change detection, parsing, and storage updates via `Parallel.ForEachAsync`
- **Project Scope** — `IProjectScopeFactory` / `ProjectScopeFactory` — creates short-lived `IProjectScope` instances per tool call. Each scope validates the path, resolves or registers the repo, and wires up the `IIndexEngine` and `ISymbolStore` for that project. Tools use `await using var scope = await _scopeFactory.CreateAsync(...)` and never hold long-lived engine/store references.
- **Registry Service** — `IRegistryService` / `RegistryService` — manages the global repository registry in the shared `~/.code-compress/index.db`. Tracks all indexed projects with file/symbol counts and last-indexed timestamps. `list_repos` exposes this to agents, **filtered to repositories within the access boundary** (see Access boundary under Security Requirements).
- **File Hasher** — `IFileHasher` / `FileHasher` — parallel SHA-256 file hashing with `ArrayPool<byte>` buffering
- **Change Tracker** — `IChangeTracker` / `ChangeTracker` — pure-function diff of current vs stored hashes, produces `ChangeSet` (new/modified/deleted/unchanged)
- **Path Validation** — `PathValidator` (static) for path traversal prevention; `IPathValidator` / `PathValidatorService` wrapper for DI/testability
- **GitIgnore Filter** — `IGitIgnoreFilter` / `GitIgnoreFilter` — filters discovered paths against `.gitignore` rules before indexing
- **Project Root Resolver** — `IProjectRootResolver` / `ProjectRootResolver` — walks up the directory tree to find the `.git` root
- **MCP Server Host** — `GenericHost` + `ModelContextProtocol` SDK, stdio transport
- **DI Registration** — `ServiceCollectionExtensions.AddCodeCompressCore()` registers all Core services

### Data Flow

```
AI Agent → MCP Prompts (workflow discovery) → Tool Router → Project Scope Factory
                                                          → Path Validator
                                                          → Registry Service (global repo list)
                                                          → Index Engine → File Hasher (parallel SHA-256)
                                                                        → GitIgnore Filter
                                                                        → Change Tracker (diff logic)
                                                                        → Language Parsers (per extension)
                                                                        → Symbol Store (SQLite, global DB)
```

### Database

Single global SQLite database at **`~/.code-compress/index.db`** — shared across all indexed projects. All tool calls use this one DB regardless of which project root is being queried.

Tables:

| Table | Purpose |
|-------|---------|
| `repositories` | Registry of all indexed projects (id, root_path, name, file_count, symbol_count, last_indexed) |
| `files` | Source files per repo (relative_path, content_hash, byte_length, line_count) |
| `symbols` | Parsed code symbols (name, kind, signature, parent_symbol, byte offsets, visibility, doc_comment) |
| `dependencies` | Import/require edges between files — `edge_kind` is typed: `imports`, `calls`, `implements`, `inherits`, `references` |
| `index_snapshots` | Named baselines for change tracking (stores file hashes + serialized symbol summaries as JSON) |

FTS5 virtual tables: `symbols_fts` (name, parent_symbol, signature, doc_comment — porter unicode61 tokenizer), `file_content_fts` (relative_path, content).

### MCP Tools (6 categories, 22 tools)

**Indexing**
| Tool | Purpose |
|------|---------|
| `index_project` | Build/update symbol database — **must call first** |
| `snapshot_create` | Create named baseline before making changes |
| `invalidate_cache` | Delete all indexed data for a project (forces full reparse) |
| `list_repos` | List all projects in the global registry |

**Query**
| Tool | Purpose |
|------|---------|
| `project_outline` | Compressed overview of all symbols, grouped by file/kind/directory |
| `topic_outline` | Search for a topic and return matching symbols in outline format |
| `get_symbol` | Retrieve full source code by qualified name (byte-offset seeking) |
| `expand_symbol` | Retrieve a single method without loading the parent class (~60% fewer tokens) |
| `get_symbols` | Batch-retrieve source for multiple symbols (max 50) |
| `get_module_api` | Public API surface of a single module/file |
| `search_symbols` | FTS5 search by name/type — PascalCase/camelCase-aware with fuzzy fallback |
| `search_text` | Search raw file contents (literals, comments, config values) |
| `get_hot_path` | Extract only lines containing specific identifiers with surrounding context (~10-40× token savings vs full symbol) |
| `assemble_context` | One-shot: search symbols + retrieve source + overview (collapses 5-10 round-trips into 1) |

**Delta**
| Tool | Purpose |
|------|---------|
| `changes_since` | Symbol-level diff since a named snapshot (new/modified/deleted) |
| `file_tree` | Annotated directory tree with file/line counts — does **not** require `index_project` |

**Dependency**
| Tool | Purpose |
|------|---------|
| `dependency_graph` | File import/dependency relationships |
| `blast_radius` | Reverse BFS — what files/symbols break if X changes |
| `find_unused_symbols` | Detect public symbols with no incoming dependency edges |
| `project_dependencies` | Inter-project dependencies in .NET solutions |
| `find_references` | All locations where a symbol is referenced |

**Context**
| Tool | Purpose |
|------|---------|
| `assemble_context` | (listed above under Query) |

**Server**
| Tool | Purpose |
|------|---------|
| `stop_server` | Gracefully shut down the MCP server |

### MCP Prompts (Progressive Tool Disclosure)

Named workflows exposed via `[McpServerPrompt]` in `Server/Prompts/PromptsProvider.cs`. Agents can discover these to understand which tools to combine:

| Prompt | Workflow |
|--------|---------|
| `ExploreCodebase` | `index_project` → `project_outline` → `search_symbols` → `get_symbol` |
| `FindImpact` | `index_project` → `blast_radius` → `find_references` → `dependency_graph` |
| `ReviewChanges` | `snapshot_create` → [make changes] → `index_project` → `changes_since` |
| `DebugSymbol` | `search_symbols` → `get_hot_path` → `get_symbol` → `find_references` |

## Development Methodology

### TDD is Mandatory

Every feature must be developed test-first using **TUnit** (source-generated, AOT-compatible):
1. Write failing test(s) defining expected behavior
2. Write minimum code to pass
3. Refactor while keeping tests green

**TUnit assertion style** — all assertions are async/fluent:
```csharp
await Assert.That(result.Symbols).Count().IsEqualTo(1);
await Assert.That(result.Symbols[0].Name).IsEqualTo("expected");
```

Use `[Arguments(...)]` attribute for parameterized tests. Use **NSubstitute** for mocking interfaces. Use **Verify** for snapshot testing complex outputs (outlines, dependency graphs).

**Test structure mirrors source:** every class in `CodeCompress.Core` has a corresponding test class in `CodeCompress.Core.Tests`.

### Coverage Targets

| Layer | Target |
|-------|--------|
| Parsers (all languages) | 95%+ |
| Storage (SQLite) | 90%+ |
| Index Engine | 90%+ |
| MCP Tools | 85%+ |

## Security Requirements (OWASP Top 10)

MCP tool parameters (`path`, `query`) are **untrusted inputs** from AI agents.

- **Path traversal prevention (A01):** All file paths must be canonicalized via `Path.GetFullPath()` + starts-with check against project root. No `..` traversal. Reject paths outside project root. Implemented in `Validation/PathValidator.cs`.
- **Access boundary (A01):** The server is clamped to a **boundary root** (the launch working directory, or `CODECOMPRESS_ROOT` if set, captured once at startup) plus an optional `CODECOMPRESS_ALLOWED_ROOTS` allowlist. Enforcement lives in `Validation/BoundaryPolicy.cs` + `IBoundaryPolicy`, applied at the single choke point `PathValidatorService.ValidatePath` (so every tool inherits it). Out-of-bounds paths throw `BoundaryViolationException : ArgumentException`, which flows through each tool's existing `catch (ArgumentException) → INVALID_PATH` as a uniform, non-leaking error. `RegistryService` filters `list_repos` and guards registry writes to in-bounds repos; `ProjectScopeFactory` clamps git-root resolution so it cannot walk above the boundary. The parameterless `PathValidatorService()` (unrestricted) is `internal` and test-only — production always uses the DI-injected boundary-aware constructor.
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

## Web Dashboard (CodeCompress.Web)

The Blazor Server dashboard lives in `src/CodeCompress.Web/`. It uses **BlazorBlueprint v3** as the component library and **Catppuccin** (Macchiato dark / Latte light) for the color palette via `IThemeService`.

### BlazorBlueprint Components — Mandatory

**Always use BB components over raw HTML.** This is enforced during code review.

| Need | Use | Do NOT use |
|------|-----|------------|
| Card / panel | `BbCard`, `BbCardHeader`, `BbCardTitle`, `BbCardDescription`, `BbCardContent`, `BbCardAction` | `<div class="card">` |
| Data table | `BbDataGrid<TData>` + `BbDataGridPropertyColumn` / `BbDataGridTemplateColumn` | `<table>` |
| Alert / banner | `BbAlert` + `BbAlertTitle` + `BbAlertDescription` (inside `<ChildContent>`) | `<div class="alert">` |
| Empty state | `BbEmpty` | custom empty divs |
| Search input | `BbInputGroup` + `BbInputGroupAddon` + `BbInputGroupInput` + `BbInputGroupButton` | raw `<input>` + `<button>` |
| Breadcrumb | `BbBreadcrumb` → `BbBreadcrumbList` → `BbBreadcrumbItem` → `BbBreadcrumbLink` / `BbBreadcrumbPage` | `<nav>` with raw links |
| Badge / tag | `BbBadge` | `<span class="badge">` |
| Button | `BbButton` | `<button>` (except inside template columns or icon-only action rows) |

**Known BB component constraints:**

- `BbEmpty`, `BbAlert`, `BbCard`, `BbBreadcrumbPage` do **not** have `AdditionalAttributes` (CaptureUnmatchedValues) — passing `data-testid` or any unknown HTML attribute causes a runtime `InvalidOperationException`. Place `data-testid` only on native HTML elements (`<div>`, `<span>`, `<a>`, `<button>`).
- `BbDataGrid` requires `TData : class` — use a `private sealed record` instead of a value tuple when the type would otherwise be a struct.
- `BbAlert` child content: `BbAlertTitle` and `BbAlertDescription` must be inside `<ChildContent>...</ChildContent>`, not as bare children.
- Razor `Class` attribute interpolation: use `Class="@($"base-class modifier--{method()}")"` — never mix C# and literal text like `Class="base modifier--@method()"`.
- `BbInputGroupInput` uses `UpdateTiming` enum: `Immediate`, `OnChange`, `Debounced` — `OnInput` does not exist.
- `BbBreadcrumbLink Href` with route params: use `Href="@($"/repo/{RepoId}")"` — not `Href="/repo/@RepoId"`.

### Theme

Theme toggling is handled by `IThemeService` (injected via DI). Do **not** use JSInterop for theme switching — BB's `ThemeService` handles it. The `ThemeToggle.razor` component calls `ThemeService.ToggleAsync()`.

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

**Available:** Luau, C#, Java, Go, TypeScript/JavaScript, Rust, Python, Terraform, Blazor Razor, .NET Project Files, JSON Config, YAML Config
**Planned:** (none currently)

## Key NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 1.2.0 | MCP SDK — server hosting, tool/prompt registration |
| `Microsoft.Data.Sqlite` | 10.0.3 | SQLite access with FTS5 |
| `Microsoft.Extensions.FileSystemGlobbing` | 10.0.3 | Glob pattern matching for file discovery |
| `Microsoft.Extensions.Hosting` | 10.0.3 | Generic host for DI, logging |
| `TreeSitter.DotNet` | 1.3.0 | Tree-sitter bindings — AST parsing for C#, Java, Go, TypeScript/JavaScript, Rust, Python |
| `System.CommandLine` | 2.0.5 | CLI argument parsing (CodeCompress.Cli) |
| `YamlDotNet` | 16.3.0 | YAML config file parsing |
| `TUnit` | 1.19.11 | Testing framework |
| `NSubstitute` | 5.3.0 | Mocking |
| `Verify` | 31.13.2 | Snapshot testing |
| `SonarAnalyzer.CSharp` | 10.20.0.135146 | Static analysis |

## Project Skills & References

Specialized Claude Code skills live in `.claude/skills/`. A shared .NET reference lives in `.claude/references/`.

### Available Skills

| Skill | Slash Command | Purpose |
|-------|--------------|---------|
| **implement-plan** | `/implement-plan` | Orchestrates feature implementation with TDD, security enforcement, and agent delegation |
| **tdd-expert** | `/tdd-expert` | TUnit testing patterns, NSubstitute mocking, Verify snapshots, coverage targets |
| **security-expert** | `/security-expert` | OWASP Top 10 + MCP-specific threats (prompt injection, data exfil, tool poisoning) |
| **cli-expert** | `/cli-expert` | Production CLI patterns, System.CommandLine, POSIX conventions, output formatting |
| **parser-expert** | `/parser-expert` | Language parser development, regex symbol extraction, sample projects, integration tests |

### Shared Reference

- **`.claude/references/dotnet-reference.md`** — Comprehensive .NET 10 / C# 14 knowledge base (naming, code style, DI patterns, async conventions, analyzer rules). All skills link to this file.

### Skill Invocation

Skills can be used in two ways:
1. **User-invoked:** `/security-expert review src/CodeCompress.Server/Tools/` — the user types the slash command directly, which loads the SKILL.md content into the conversation
2. **Agent delegation:** Read the skill's SKILL.md file, then launch an Agent with the full skill content and all relevant source code inlined in the prompt. Sub-agents cannot call MCP tools or read files — everything they need must be in the prompt. See Step 4 in implement-plan for the detailed delegation procedure.

### Security Skill is Mandatory

The **security-expert** skill must be engaged on **every** implementation task — either in review mode (post-implementation audit) or enforce mode (during implementation). This is not optional. To engage the skill: read `.claude/skills/security-expert/SKILL.md`, then delegate to an Agent with the full skill content and all modified source code inlined in the prompt. Security is a first-class concern for this project given that MCP tool parameters are untrusted agent inputs and tool outputs can be weaponized via prompt injection.
