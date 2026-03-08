# CodeCompress — Intelligent Code Index MCP Server for AI Agents

**Version:** 1.0
**Status:** Draft
**Created:** March 7, 2026
**Author:** MCrank
**Platform:** .NET 10 / C# 14 / Cross-platform
**License:** MIT (standalone open-source tool)

---

## 1. Executive Summary

CodeCompress is a lightweight Model Context Protocol (MCP) server that indexes codebases and provides AI agents with compressed, surgical access to code symbols — eliminating the need for full file scans at the start of every context window.

The core value proposition: **reduce AI agent token consumption by 80-90%** when loading codebase context, by replacing "read every file" with targeted symbol lookups, compressed outlines, and delta tracking.

### Problem Statement

AI coding agents (Claude Code, Cursor, etc.) start each context window with no memory of the codebase. They compensate by scanning source files to build understanding before doing real work. As codebases grow, this scanning phase:

- Consumes 30-150k+ tokens per session just for context loading
- Multiplies when sub-agents each independently scan the same files
- Adds 2-5 minutes of latency before productive work begins
- Gets worse linearly with codebase size — O(N) per session

### Solution

CodeCompress maintains a persistent index of the codebase (symbols, types, dependencies, API surfaces) in a local SQLite database. AI agents query the index via MCP tools to get exactly the context they need — a `project_outline` returns the full API surface in ~3-5k tokens regardless of codebase size, and `get_symbol` retrieves individual functions by byte offset.

### Target Languages

**Phase 1 (MVP):** Luau (Roblox game development)
**Phase 2:** C# / .NET
**Future:** Python, TypeScript/JavaScript, Go, Rust, and beyond

### Extensibility

Adding a new language requires **one class** implementing `ILanguageParser` — no changes to storage, indexing, querying, or MCP tools. The `IndexEngine` resolves parsers by file extension automatically via DI.

| Language   | Estimated Effort | Regex Complexity                                                                             |
| ---------- | ---------------- | -------------------------------------------------------------------------------------------- |
| Python     | ~3-4h            | Low — `def`, `class`, `import`, type hints. Indentation-scoping is the main challenge.       |
| TypeScript | ~4-5h            | Medium — `export`, `interface`, `type`, arrow fns, `import`. Shares patterns with C# parser. |
| JavaScript | ~2-3h            | Low — TypeScript parser minus type annotations.                                              |
| Go         | ~3-4h            | Low — Very regular syntax: `func`, `type`, `struct`, `interface`, `import`.                  |
| Rust       | ~4-5h            | Medium — `fn`, `struct`, `enum`, `impl`, `trait`, `mod`, `use`. Generics add complexity.     |

---

## 2. Architecture

### 2.1 High-Level Design

```text
┌──────────────────┐     stdio/HTTP      ┌──────────────────────┐
│                  │ ◄─────────────────► │                      │
│  Claude Code     │     MCP Protocol    │  CodeCompress Server │
│  (AI Agent)      │                     │                      │
│                  │                     │  ┌────────────────┐  │
└───────────────── ┘                     │  │  Tool Router   │  │
                                         │  └───────┬────────┘  │
                                         │          │           │
                                         │  ┌───────▼────────┐  │
                                         │  │  Index Engine  │  │
                                         │  │  ┌───────────┐ │  │
                                         │  │  │  Parsers  │ │  │
                                         │  │  │  ├─ Luau  │ │  │
                                         │  │  │  └─ C#    │ │  │
                                         │  │  └───────────┘ │  │
                                         │  └───────┬────────┘  │
                                         │          │           │
                                         │ ┌────────▼────────┐  │
                                         │ │  SQLite Store   │  │
                                         │ │(~/.codecompress)│  │
                                         │ └─────────────────┘  │
                                         └──────────────────────┘
```

### 2.2 Core Components

| Component            | Responsibility                         | .NET Pattern                                    |
| -------------------- | -------------------------------------- | ----------------------------------------------- |
| **MCP Server Host**  | Transport, tool registration, DI       | `GenericHost` + `ModelContextProtocol` SDK      |
| **Tool Router**      | Dispatches MCP tool calls to handlers  | Attribute-based `[McpServerTool]` methods       |
| **Index Engine**     | Orchestrates parsing, storage, queries | Singleton service via DI                        |
| **Language Parsers** | Extract symbols from source files      | Strategy pattern (`ILanguageParser`)            |
| **Symbol Store**     | SQLite persistence, FTS5, queries      | Repository pattern over `Microsoft.Data.Sqlite` |
| **Change Tracker**   | SHA-256 file hashing, delta detection  | Integrated into Index Engine                    |

### 2.3 Data Model

```text
┌─────────────────────────────────┐
│ repositories                    │
│ ─────────────────────────────── │
│ id: TEXT (SHA-256 of root path) │
│ root_path: TEXT                 │
│ name: TEXT                      │
│ language: TEXT                  │
│ last_indexed: INTEGER           │
│ file_count: INTEGER             │
│ symbol_count: INTEGER           │
└──────────┬──────────────────────┘
           │ 1:N
┌──────────▼──────────────────────┐
│ files                           │
│ ─────────────────────────────── │
│ id: INTEGER PK                  │
│ repo_id: TEXT FK                │
│ relative_path: TEXT             │
│ content_hash: TEXT (SHA-256)    │
│ byte_length: INTEGER            │
│ line_count: INTEGER             │
│ last_modified: INTEGER          │
│ indexed_at: INTEGER             │
└──────────┬──────────────────────┘
           │ 1:N
┌──────────▼──────────────────────┐
│ symbols                         │
│ ─────────────────────────────── │
│ id: INTEGER PK                  │
│ file_id: INTEGER FK             │
│ name: TEXT                      │
│ kind: TEXT                      │
│   (function|method|type|class|  │
│    interface|export|constant|   │
│    module)                      │
│ signature: TEXT                 │
│ parent_symbol: TEXT (nullable)  │
│ byte_offset: INTEGER            │
│ byte_length: INTEGER            │
│ line_start: INTEGER             │
│ line_end: INTEGER               │
│ visibility: TEXT                │
│   (public|private|local)        │
│ doc_comment: TEXT (nullable)    │
└─────────────────────────────────┘

┌─────────────────────────────────┐
│ dependencies                    │
│ ─────────────────────────────── │
│ id: INTEGER PK                  │
│ file_id: INTEGER FK             │
│ requires_path: TEXT             │
│ resolved_file_id: INTEGER FK    │
│ alias: TEXT                     │
└─────────────────────────────────┘

┌─────────────────────────────────┐
│ index_snapshots                 │
│ ─────────────────────────────── │
│ id: INTEGER PK                  │
│ repo_id: TEXT FK                │
│ snapshot_label: TEXT            │
│ created_at: INTEGER             │
│ file_hashes: TEXT (JSON blob)   │
└─────────────────────────────────┘

FTS5 Virtual Tables:
  symbols_fts (name, signature, doc_comment)
  file_content_fts (relative_path, content)
```

---

## 3. MCP Tools Specification

### 3.1 Indexing Tools

#### `index_project`

Indexes a local project directory. Uses SHA-256 hashing for incremental updates — only re-parses files that have changed since the last index.

| Parameter          | Type     | Required | Description                                                                       |
| ------------------ | -------- | -------- | --------------------------------------------------------------------------------- |
| `path`             | string   | Yes      | Absolute path to project root                                                     |
| `language`         | string   | No       | Filter to one language (`luau`, `csharp`). Omit to index all supported languages. |
| `include_patterns` | string[] | No       | Glob patterns to include (default: all source files)                              |
| `exclude_patterns` | string[] | No       | Glob patterns to exclude (default: common ignores)                                |

**Returns:** `{ repo_id, files_indexed, files_skipped, symbols_found, duration_ms }`

**Language resolution (automatic, no configuration required):**

All registered `ILanguageParser` implementations declare their file extensions via DI. At startup, the `IndexEngine` builds an extension → parser lookup map (e.g., `.luau` → `LuauParser`, `.cs` → `CSharpParser`). During indexing:

1. Each source file's extension is matched against the parser map
2. If a parser exists → parse the file, tag symbols with the parser's `LanguageId`
3. If no parser exists → skip the file (non-source: `.md`, `.json`, `.toml`, etc.)
4. Mixed-language projects are indexed automatically — every file gets the right parser
5. If the `language` parameter is set, only files matching that parser's extensions are indexed (useful for filtering in polyglot repos)

**Default excludes:** `.git/`, `node_modules/`, `bin/`, `obj/`, `Packages/`, `build/`, `*.rbxlx`, `*.rbxl`

#### `snapshot_create`

Creates a named snapshot of the current index state, used for delta tracking between mini-plan executions.

| Parameter | Type   | Required | Description                                                |
| --------- | ------ | -------- | ---------------------------------------------------------- |
| `path`    | string | Yes      | Project root path                                          |
| `label`   | string | Yes      | Snapshot label (e.g., `"mini-plan-001"`, `"pre-refactor"`) |

**Returns:** `{ snapshot_id, label, file_count, symbol_count }`

#### `invalidate_cache`

Forces full re-indexing on next `index_project` call.

| Parameter | Type   | Required | Description       |
| --------- | ------ | -------- | ----------------- |
| `path`    | string | Yes      | Project root path |

**Returns:** `{ success, message }`

### 3.2 Query Tools

#### `project_outline`

**The primary context-loading tool.** Returns a compressed summary of the entire project — every module, its public API surface (function signatures + types), and dependency relationships. Designed to replace "scan every file" with a single call.

| Parameter         | Type   | Required | Description                                    |
| ----------------- | ------ | -------- | ---------------------------------------------- |
| `path`            | string | Yes      | Project root path                              |
| `include_private` | bool   | No       | Include private/local symbols (default: false) |
| `group_by`        | string | No       | `"file"` (default), `"kind"`, `"directory"`    |
| `max_depth`       | int    | No       | Directory depth limit (default: unlimited)     |

**Returns:** Structured markdown/text outline:

```text
# Project Outline: corporate-wars (80 files, 1,247 symbols)

## src/server/Services/
### CombatService.luau (12 symbols)
  class CombatService
    :Initialize() -> ()
    :ProcessAttack(attackerId: string, targetId: string, weaponId: string) -> DamageResult
    :CalculateDamage(weapon: WeaponData, distance: number, armor: number) -> number
    :CheckLineOfSight(origin: Vector3, target: Vector3) -> boolean
    :Destroy() -> ()

### AIService.luau (18 symbols)
  class AIService
    :Initialize() -> ()
    :UpdateEnemies(dt: number) -> ()
    ...

## src/shared/Types/
### GameTypes.luau (15 types)
  export type AgentData = { id: string, name: string, health: number, ... }
  export type WeaponData = { id: string, damage: number, fireRate: number, ... }
  export type EnemyData = { ... }
  ...

## Dependencies:
  CombatService.luau → GameTypes.luau, WeaponConfig.luau
  AIService.luau → GameTypes.luau, EnemyConfig.luau, CombatService.luau
  ...
```

**Target: ~3-8k tokens** for a full project outline regardless of codebase size (signatures only, no implementation bodies).

#### `get_symbol`

Retrieves the full source code of a single symbol using byte-offset seeking. Reads only the exact bytes needed — not the entire file.

| Parameter         | Type   | Required | Description                                                           |
| ----------------- | ------ | -------- | --------------------------------------------------------------------- |
| `path`            | string | Yes      | Project root path                                                     |
| `symbol_name`     | string | Yes      | Fully qualified name (e.g., `"CombatService:ProcessAttack"`)          |
| `include_context` | bool   | No       | Include 5 lines before/after for surrounding context (default: false) |

**Returns:** `{ name, kind, file, line_start, line_end, signature, source_code }`

#### `get_symbols`

Batch retrieval of multiple symbols in a single call. More efficient than multiple `get_symbol` calls.

| Parameter      | Type     | Required | Description                          |
| -------------- | -------- | -------- | ------------------------------------ |
| `path`         | string   | Yes      | Project root path                    |
| `symbol_names` | string[] | Yes      | List of fully qualified symbol names |

**Returns:** Array of symbol results (same shape as `get_symbol`)

#### `get_module_api`

Returns the complete public API surface of a single module — all exported functions, types, and constants with their signatures. More detailed than `project_outline` for a single file.

| Parameter     | Type   | Required | Description                                                                     |
| ------------- | ------ | -------- | ------------------------------------------------------------------------------- |
| `path`        | string | Yes      | Project root path                                                               |
| `module_path` | string | Yes      | Relative path to module file (e.g., `"src/server/Services/CombatService.luau"`) |

**Returns:** Full signature listing with doc comments, parameter types, return types, and the module's require() dependencies.

#### `dependency_graph`

Returns the project's dependency graph — who requires/imports whom.

| Parameter   | Type   | Required | Description                                                                    |
| ----------- | ------ | -------- | ------------------------------------------------------------------------------ |
| `path`      | string | Yes      | Project root path                                                              |
| `root_file` | string | No       | Start from a specific file (default: full project graph)                       |
| `direction` | string | No       | `"both"` (default), `"dependents"` (who uses me), `"dependencies"` (who I use) |
| `depth`     | int    | No       | Max traversal depth (default: unlimited)                                       |

**Returns:** Adjacency list with metadata:

```text
CombatService.luau
  requires → GameTypes.luau, WeaponConfig.luau, Constants/Combat.luau
  required by → AgentService.luau, MissionService.luau
```

#### `search_symbols`

Full-text search across symbol names, signatures, and doc comments using SQLite FTS5.

| Parameter | Type   | Required | Description                                                            |
| --------- | ------ | -------- | ---------------------------------------------------------------------- |
| `path`    | string | Yes      | Project root path                                                      |
| `query`   | string | Yes      | Search query (supports FTS5 syntax: `"damage OR health"`, `"combat*"`) |
| `kind`    | string | No       | Filter by symbol kind: `function`, `type`, `class`, etc.               |
| `limit`   | int    | No       | Max results (default: 20)                                              |

**Returns:** Ranked results with file path, line number, signature, and match context.

#### `search_text`

Raw full-text search across file contents. Fallback for when symbol search isn't enough.

| Parameter | Type   | Required | Description                                               |
| --------- | ------ | -------- | --------------------------------------------------------- |
| `path`    | string | Yes      | Project root path                                         |
| `query`   | string | Yes      | Search text                                               |
| `glob`    | string | No       | File pattern filter (e.g., `"*.luau"`, `"src/server/**"`) |
| `limit`   | int    | No       | Max results (default: 20)                                 |

**Returns:** File matches with line numbers and context snippets.

### 3.3 Delta Tools

#### `changes_since`

**The delta-tracking tool.** Compares current codebase state against a named snapshot and returns what changed — new files, modified files, deleted files, and which symbols were added/modified/removed.

| Parameter        | Type   | Required | Description                                             |
| ---------------- | ------ | -------- | ------------------------------------------------------- |
| `path`           | string | Yes      | Project root path                                       |
| `snapshot_label` | string | Yes      | Compare against this snapshot (e.g., `"mini-plan-001"`) |

**Returns:**

```text
Changes since snapshot "mini-plan-001":

New files (3):
  + src/server/Services/WeaponService.luau (8 symbols)
  + src/shared/Types/CombatTypes.luau (5 types)
  + src/shared/Constants/WeaponConfig.luau (3 constants)

Modified files (1):
  ~ src/server/init.server.luau
    + Added: require(WeaponService), require(CombatTypes)

Deleted files (0): none

Symbol changes:
  + WeaponService:Initialize() -> ()
  + WeaponService:GetWeaponData(weaponId: string) -> WeaponData?
  + WeaponService:FireWeapon(agentId: string, weaponId: string) -> FiringResult
  + export type WeaponData = { ... }
  + export type FiringResult = { ... }
  ~ CombatService:ProcessAttack — signature changed (added weaponId param)
```

**Target: ~1-3k tokens** for a typical mini-plan delta.

#### `file_tree`

Returns the project file tree with metadata (file count, line count per directory). Lighter weight than `project_outline`.

| Parameter   | Type   | Required | Description                        |
| ----------- | ------ | -------- | ---------------------------------- |
| `path`      | string | Yes      | Project root path                  |
| `max_depth` | int    | No       | Directory depth limit (default: 5) |

**Returns:** Annotated file tree.

---

## 4. Mandatory Requirements

### 4.1 Test-Driven Development (TDD)

**TDD is mandatory.** Every feature must be developed test-first:

1. Write failing test(s) that define the expected behavior
2. Write the minimum code to make the test(s) pass
3. Refactor while keeping tests green

**Testing Framework:** [TUnit](https://tunit.dev/) — a modern .NET testing framework leveraging source generation and AOT compilation.

**TUnit patterns:**

```csharp
[McpServerToolType]
public sealed class LuauParserTests
{
    [Test]
    public async Task Parse_FunctionDeclaration_ExtractsSignature()
    {
        var parser = new LuauParser();
        var content = "function CombatService:Attack(targetId: string): DamageResult"u8;
        var result = parser.Parse("test.luau", content);

        await Assert.That(result.Symbols).HasCount(1);
        await Assert.That(result.Symbols[0].Name).IsEqualTo("CombatService:Attack");
        await Assert.That(result.Symbols[0].Kind).IsEqualTo(SymbolKind.Method);
        await Assert.That(result.Symbols[0].Signature)
            .Contains("targetId: string")
            .And.Contains("DamageResult");
    }

    [Test]
    [Arguments("export type AgentData = { id: string }", SymbolKind.Type, Visibility.Public)]
    [Arguments("type InternalState = { count: number }", SymbolKind.Type, Visibility.Private)]
    [Arguments("local function validate(x: string): boolean", SymbolKind.Function, Visibility.Private)]
    public async Task Parse_SymbolVisibility_CorrectlyClassified(
        string code, SymbolKind expectedKind, Visibility expectedVisibility)
    {
        var parser = new LuauParser();
        var result = parser.Parse("test.luau", Encoding.UTF8.GetBytes(code));

        await Assert.That(result.Symbols[0].Kind).IsEqualTo(expectedKind);
        await Assert.That(result.Symbols[0].Visibility).IsEqualTo(expectedVisibility);
    }
}
```

**NuGet package:** `TUnit` (single package, includes runner + assertions)

**Test coverage targets:**

| Layer              | Coverage Target | Focus                                             |
| ------------------ | --------------- | ------------------------------------------------- |
| Parsers (Luau, C#) | 95%+            | Every symbol pattern, edge cases, malformed input |
| Storage (SQLite)   | 90%+            | CRUD, FTS5 queries, migrations, concurrent access |
| Index Engine       | 90%+            | Incremental indexing, hashing, change detection   |
| MCP Tools          | 85%+            | Tool parameter validation, output formatting      |
| Integration        | Key paths       | End-to-end: index → query → verify                |

**Test project structure mirrors source:** every class in `CodeCompress.Core` has a corresponding test class in `CodeCompress.Core.Tests`.

### 4.2 Security (OWASP Top 10)

**Security is mandatory.** The MCP server accepts file paths and search queries from AI agents — these are untrusted inputs.

| OWASP Category                 | Relevance                                       | Mitigation                                                                                           |
| ------------------------------ | ----------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| A01: Broken Access Control     | **High** — path traversal via `path` parameters | Canonicalize and validate all paths. Reject paths outside project root. No `..` traversal.           |
| A02: Cryptographic Failures    | Low — no secrets stored                         | SHA-256 for file hashing only (not security-critical)                                                |
| A03: Injection                 | **High** — FTS5 query injection, SQL injection  | Parameterized queries everywhere. Never interpolate user input into SQL. Sanitize FTS5 query syntax. |
| A04: Insecure Design           | Medium                                          | Principle of least privilege — read-only access to source files. No file modification tools.         |
| A05: Security Misconfiguration | Medium                                          | Secure SQLite defaults (WAL mode, no world-readable permissions). Validate config inputs.            |
| A06: Vulnerable Components     | Medium                                          | Central Package Management with pinned versions. Dependabot/GitHub security alerts.                  |
| A07: Auth Failures             | Low — local stdio transport                     | No auth needed for stdio. HTTP transport (future): require auth token.                               |
| A08: Data Integrity Failures   | Medium                                          | Validate SHA-256 hashes. Verify file existence before reading. Handle TOCTOU races.                  |
| A09: Logging Failures          | Medium                                          | Structured logging via `ILogger`. Log all indexing operations. Never log file contents.              |
| A10: SSRF                      | Low — no outbound network calls                 | No remote fetching in MVP. Future: validate URLs for remote repo support.                            |

**Code-level requirements:**

- All SQL uses parameterized queries (`@param` syntax) — zero string concatenation
- All file paths validated via `Path.GetFullPath()` + starts-with check against project root
- FTS5 queries sanitized to prevent syntax injection
- No `dynamic` or `object` types for user-facing data
- `SonarAnalyzer.CSharp` enforces rules at build time

### 4.3 Performance Requirements

**Application performance:**

- Async/await throughout — no blocking calls
- `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` for file parsing (zero-copy where possible)
- `Parallel.ForEachAsync` for file hashing and parsing during full index
- Connection pooling for SQLite
- Lazy initialization for expensive resources

**SQL performance:**

- Indexes on all FK columns and commonly queried fields (`symbols.name`, `symbols.kind`, `files.content_hash`)
- FTS5 for text search instead of `LIKE '%query%'`
- Batch inserts via transactions (not individual INSERTs)
- `PRAGMA journal_mode=WAL` for concurrent read performance
- `PRAGMA synchronous=NORMAL` for write performance (acceptable for a local cache — data is always re-derivable)
- Query execution plans reviewed for all common queries
- Prepared statements cached where possible

---

## 5. Language Parser Specifications

### 5.1 Parser Interface

```csharp
public interface ILanguageParser
{
    string LanguageId { get; }             // "luau", "csharp"
    string[] FileExtensions { get; }       // [".luau"], [".cs"]
    ParseResult Parse(string filePath, ReadOnlySpan<byte> content);
}

public record ParseResult(
    IReadOnlyList<SymbolInfo> Symbols,
    IReadOnlyList<DependencyInfo> Dependencies
);

public record SymbolInfo(
    string Name,
    SymbolKind Kind,
    string Signature,
    string? ParentSymbol,
    int ByteOffset,
    int ByteLength,
    int LineStart,
    int LineEnd,
    Visibility Visibility,
    string? DocComment
);

public record DependencyInfo(
    string RequirePath,   // raw require() argument
    string? Alias         // local variable name it's assigned to
);
```

### 5.2 Luau Parser

**Approach:** Regex/pattern-based extraction. Luau's syntax is regular enough that full AST parsing is unnecessary for symbol extraction.

**Symbols to extract:**

| Symbol Kind    | Pattern                                             | Example                                           |
| -------------- | --------------------------------------------------- | ------------------------------------------------- |
| Module class   | `local ClassName = {} :: ClassName`                 | `local CombatService = {} :: CombatService`       |
| Method         | `function ClassName:MethodName(params): ReturnType` | `function CombatService:Initialize(): ()`         |
| Function       | `function functionName(params): ReturnType`         | `function calculateDamage(w: WeaponData): number` |
| Local function | `local function name(params): ReturnType`           | `local function validate(input: string): boolean` |
| Export type    | `export type TypeName = { ... }`                    | `export type AgentData = { id: string, ... }`     |
| Local type     | `type TypeName = { ... }`                           | `type InternalState = { ... }`                    |
| Constant       | `local CONSTANT_NAME = value` (SCREAMING_SNAKE)     | `local MAX_HEALTH = 100`                          |
| Module return  | `return ClassName`                                  | `return CombatService`                            |

**Dependencies to extract:**

| Pattern                                              | Example                                                                |
| ---------------------------------------------------- | ---------------------------------------------------------------------- |
| `local X = require(path.to.Module)`                  | `local Types = require(game.ReplicatedStorage.Shared.Types.GameTypes)` |
| `local Service = require(script.Parent.ServiceName)` | `local CombatService = require(script.Parent.CombatService)`           |

**Visibility rules:**

- `export type` → public
- Functions on the returned class (`ClassName:Method`) → public
- `local function` → private
- `type` (without export) → private

**Edge cases to handle:**

- Multi-line type definitions (track brace depth)
- Generic types: `export type Result<T> = { ok: boolean, value: T? }`
- Union types: `type Status = "active" | "downed" | "dead"`
- Metatable setup: `(setmetatable :: any)(self, ClassName)` — identifies OOP class
- String requires vs path requires

### 5.3 C# Parser

**Approach:** Regex/pattern-based extraction for Phase 2. C# has more complex syntax but the common patterns are well-defined.

**Symbols to extract:**

| Symbol Kind | Pattern                                                 |
| ----------- | ------------------------------------------------------- |
| Namespace   | `namespace Foo.Bar` or `namespace Foo.Bar { ... }`      |
| Class       | `[modifiers] class ClassName [: BaseClass, IInterface]` |
| Interface   | `[modifiers] interface IName`                           |
| Record      | `[modifiers] record RecordName(params)`                 |
| Method      | `[modifiers] ReturnType MethodName(params)`             |
| Property    | `[modifiers] Type PropertyName { get; set; }`           |
| Enum        | `[modifiers] enum EnumName { ... }`                     |
| Using       | `using Namespace;` or `using Alias = Namespace.Type;`   |

**Visibility:** Derived from access modifiers (`public`, `internal`, `private`, `protected`).

**Note:** For very complex C# codebases, a future Phase 3 could integrate Roslyn for true semantic analysis. The regex approach covers 90%+ of standard patterns.

---

## 6. Storage & Indexing

### 6.1 SQLite Configuration

- **Database location:** `~/.codecompress/{repo-hash}.db`
- **Repo hash:** SHA-256 of the normalized absolute path to the project root
- **WAL mode** enabled for concurrent read performance
- **FTS5** for full-text search on symbols and file content

### 6.2 Incremental Indexing Algorithm

```text
index_project(path):
  1. Hash all source files (SHA-256) in parallel
  2. Load stored hashes from the files table
  3. Diff:
     - New files (hash exists on disk, not in DB) → parse + insert
     - Modified files (hash differs) → parse + update
     - Deleted files (hash in DB, not on disk) → delete symbols + file record
     - Unchanged files → skip entirely
  4. Update repository metadata (file_count, symbol_count, last_indexed)
  5. Return summary
```

**Performance target:** Re-index a 100-file project with 5 changed files in <500ms.

### 6.3 Snapshot Mechanism

Snapshots store the complete set of `{file_path: content_hash}` pairs as a JSON blob. The `changes_since` tool diffs the current file hashes against the snapshot's stored hashes to determine what changed, then queries the symbols table for details.

---

## 7. Project Structure

```text
CodeCompress/
├── CodeCompress.sln
├── Directory.Packages.props          → Central Package Management (all versions here)
├── Directory.Build.props             → Shared build properties (TFM, analyzers, Nullable, ImplicitUsings)
├── global.json                       → .NET 10 SDK version pin
├── .editorconfig                     → Code style enforcement (C# conventions, naming, formatting)
├── nuget.config                      → NuGet feed configuration
│
├── src/
│   ├── CodeCompress.Core/              → Core library (parsers, indexing, storage)
│   │   ├── CodeCompress.Core.csproj
│   │   ├── Parsers/
│   │   │   ├── ILanguageParser.cs
│   │   │   ├── LuauParser.cs
│   │   │   └── CSharpParser.cs
│   │   ├── Indexing/
│   │   │   ├── IndexEngine.cs
│   │   │   ├── FileHasher.cs
│   │   │   └── ChangeTracker.cs
│   │   ├── Storage/
│   │   │   ├── SymbolStore.cs
│   │   │   ├── Migrations.cs
│   │   │   └── SqliteConnectionFactory.cs
│   │   ├── Models/
│   │   │   ├── SymbolInfo.cs
│   │   │   ├── DependencyInfo.cs
│   │   │   ├── ParseResult.cs
│   │   │   └── ProjectOutline.cs
│   │   └── Validation/
│   │       └── PathValidator.cs      → Path traversal prevention, canonicalization
│   │
│   ├── CodeCompress.Server/            → MCP server executable
│   │   ├── CodeCompress.Server.csproj
│   │   ├── Program.cs
│   │   └── Tools/
│   │       ├── IndexingTools.cs      → [McpServerToolType]: index_project, snapshot_create, invalidate_cache
│   │       ├── QueryTools.cs         → [McpServerToolType]: project_outline, get_symbol, get_symbols, get_module_api, search_symbols, search_text
│   │       ├── DeltaTools.cs         → [McpServerToolType]: changes_since, file_tree
│   │       └── DependencyTools.cs    → [McpServerToolType]: dependency_graph
│   │
│   └── CodeCompress.Cli/               → Optional standalone CLI (for testing/debugging)
│       ├── CodeCompress.Cli.csproj
│       └── Program.cs
│
├── tests/
│   ├── CodeCompress.Core.Tests/        → Unit tests (TUnit)
│   │   ├── CodeCompress.Core.Tests.csproj
│   │   ├── Parsers/
│   │   │   ├── LuauParserTests.cs
│   │   │   └── CSharpParserTests.cs
│   │   ├── Indexing/
│   │   │   ├── IndexEngineTests.cs
│   │   │   └── FileHasherTests.cs
│   │   ├── Storage/
│   │   │   └── SymbolStoreTests.cs
│   │   └── Validation/
│   │       └── PathValidatorTests.cs → Path traversal attack tests
│   │
│   ├── CodeCompress.Server.Tests/      → Tool-level tests (TUnit)
│   │   ├── CodeCompress.Server.Tests.csproj
│   │   └── Tools/
│   │       ├── IndexingToolsTests.cs
│   │       ├── QueryToolsTests.cs
│   │       ├── DeltaToolsTests.cs
│   │       └── DependencyToolsTests.cs
│   │
│   └── CodeCompress.Integration.Tests/ → End-to-end tests (TUnit)
│       ├── CodeCompress.Integration.Tests.csproj
│       └── EndToEndTests.cs
│
├── samples/
│   ├── luau-sample-project/          → Small Roblox project for testing
│   └── csharp-sample-project/        → Small .NET project for testing
│
├── .github/
│   └── workflows/
│       ├── ci.yml                    → Build + test + SonarAnalyzer on every PR
│       └── release.yml               → Publish dotnet tool to NuGet
│
└── README.md
```

---

## 8. Package Management & Tooling

### 8.1 Central Package Management

All package versions are declared in `Directory.Packages.props` at the solution root. Individual `.csproj` files reference packages without version numbers.

**`Directory.Packages.props`:**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- MCP SDK -->
    <PackageVersion Include="ModelContextProtocol" Version="*-*" />
    <PackageVersion Include="ModelContextProtocol.AspNetCore" Version="*-*" />

    <!-- Data -->
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="*" />

    <!-- Hosting & DI -->
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="*" />

    <!-- Code Quality (applied via Directory.Build.props) -->
    <PackageVersion Include="SonarAnalyzer.CSharp" Version="*" />

    <!-- Testing -->
    <PackageVersion Include="TUnit" Version="*" />
    <PackageVersion Include="NSubstitute" Version="*" />
    <PackageVersion Include="Verify" Version="*" />
  </ItemGroup>
</Project>
```

> **Note:** `Version="*"` above is a placeholder. At project creation time, resolve all packages to their latest stable versions for .NET 10 and pin to exact versions (e.g., `Version="10.0.0"`). Use `*-*` for MCP SDK if only pre-release versions target .NET 10.

### 8.2 Directory.Build.props

Shared build configuration applied to all projects in the solution:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <!-- SonarAnalyzer applied to all projects -->
  <ItemGroup>
    <PackageReference Include="SonarAnalyzer.CSharp" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### 8.3 global.json

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

### 8.4 .editorconfig

Standard C# `.editorconfig` enforcing:

- **Naming conventions:** PascalCase for public members, `_camelCase` for private fields, `I` prefix for interfaces
- **Formatting:** Allman brace style, 4-space indentation, spaces over tabs
- **Code style:** `var` when type is apparent, expression-bodied members for single-line, `readonly` fields where possible
- **Severity:** Naming violations = error, style violations = warning, SonarAnalyzer rules = warning (critical rules = error)
- **File header:** Required license/copyright header (optional, configure per preference)

### 8.5 NuGet Package Summary

| Package                           | Purpose                                                            | Project                         |
| --------------------------------- | ------------------------------------------------------------------ | ------------------------------- |
| `ModelContextProtocol`            | MCP SDK — server hosting, tool registration, DI                    | Server                          |
| `ModelContextProtocol.AspNetCore` | HTTP transport (optional, for non-stdio usage)                     | Server                          |
| `Microsoft.Data.Sqlite`           | SQLite access with FTS5 support                                    | Core                            |
| `Microsoft.Extensions.Hosting`    | Generic host for DI, logging, lifecycle                            | Server                          |
| `SonarAnalyzer.CSharp`            | Static analysis — security, reliability, maintainability rules     | All (via Directory.Build.props) |
| `TUnit`                           | Testing framework — source-generated, AOT-compatible               | Tests                           |
| `NSubstitute`                     | Mocking framework for interface-based testing                      | Tests                           |
| `Verify`                          | Snapshot testing for complex outputs (outlines, dependency graphs) | Tests                           |

**No external parser dependencies.** Both Luau and C# parsers use regex/pattern matching — no tree-sitter bindings needed.
**SHA-256 hashing uses `System.Security.Cryptography.SHA256`** — built into .NET, no external package required.

---

## 9. MCP Client Configuration

### Claude Code (stdio transport)

Add to `.claude/settings.json` or project settings:

```json
{
  "mcpServers": {
    "codecompress": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/CodeCompress.Server"],
      "env": {}
    }
  }
}
```

Or if published as a tool:

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

---

## 10. Build Phases

All phases follow TDD: tests are written before implementation code.

### Phase 1: Luau MVP (~3-4 days)

| Task                                        | Effort | TDD Notes                                                                                                                                                                                            |
| ------------------------------------------- | ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Project scaffold + tooling                  | 1-2h   | Solution, Directory.Packages.props, Directory.Build.props, .editorconfig, global.json, SonarAnalyzer, TUnit setup                                                                                    |
| SQLite storage + tests                      | 3-4h   | Tests first: CRUD, FTS5 queries, migrations, WAL mode, concurrent access. Then implement.                                                                                                            |
| Path validation + tests                     | 1-2h   | Tests first: traversal attacks (`../../etc/passwd`), canonicalization, symlink handling. Then implement.                                                                                             |
| Luau parser + tests                         | 4-6h   | Tests first: one test per symbol pattern (function, method, type, constant, require). Parametrized tests for visibility rules. Edge case tests (multi-line types, generics, unions). Then implement. |
| `index_project` tool + tests                | 2-3h   | Tests first: full index, incremental index (changed/new/deleted files), hash verification. Then implement.                                                                                           |
| `project_outline` tool + tests              | 2-3h   | Tests first: output format, grouping, depth limits, token size verification. Snapshot tests via Verify. Then implement.                                                                              |
| `get_symbol` / `get_symbols` + tests        | 2h     | Tests first: byte-offset accuracy, context lines, batch retrieval, missing symbol handling. Then implement.                                                                                          |
| `get_module_api` tool + tests               | 1-2h   | Tests first: complete API surface, doc comments, dependency listing. Then implement.                                                                                                                 |
| `dependency_graph` tool + tests             | 2-3h   | Tests first: require chain resolution, direction filtering, depth limits, circular dependency handling. Then implement.                                                                              |
| `search_symbols` + `search_text` + tests    | 2h     | Tests first: FTS5 query syntax, result ranking, kind filtering, injection prevention. Then implement.                                                                                                |
| `changes_since` + `snapshot_create` + tests | 2-3h   | Tests first: snapshot creation, delta detection (new/modified/deleted), symbol-level diffing. Then implement.                                                                                        |
| `file_tree` + `invalidate_cache` + tests    | 1h     | Tests first: depth limits, metadata accuracy, cache clearing. Then implement.                                                                                                                        |
| Integration tests with Corporate Wars       | 2-3h   | End-to-end: index real Roblox project → query → verify output format and token budget                                                                                                                |
| Security review + SonarAnalyzer clean       | 1-2h   | Zero SonarAnalyzer warnings. Path validation audit. SQL injection audit.                                                                                                                             |

### Phase 2: C# Support (~2-3 days)

| Task                                | Effort | TDD Notes                                                                                                                 |
| ----------------------------------- | ------ | ------------------------------------------------------------------------------------------------------------------------- |
| C# parser + tests                   | 4-6h   | Tests first: class, interface, record, method, property, enum, namespace, using patterns. Modifier parsing. Nested types. |
| Language auto-detection + tests     | 1-2h   | Tests first: .luau detection, .csproj/.sln detection, mixed project handling. Then implement.                             |
| Integration tests with .NET project | 2-3h   | End-to-end against a real C# project                                                                                      |
| Security review for C# parser       | 1h     | Ensure no parser-specific injection vectors                                                                               |

### Phase 3: Polish & Distribution (future)

- CLI tool for manual inspection/debugging
- `dotnet tool install -g codecompress` global tool distribution
- GitHub Actions CI/CD (build + test + SonarAnalyzer on every PR)
- HTTP transport option for non-stdio clients
- Performance profiling and optimization
- Code coverage reporting (target: 90%+ overall)

---

## 11. Success Criteria

**Functional:**

| Metric                         | Target                                         |
| ------------------------------ | ---------------------------------------------- |
| `project_outline` token output | <8k tokens for 80-file Luau project            |
| `project_outline` token output | <12k tokens for 200-file C# project            |
| `get_symbol` response          | <500 tokens for a single function              |
| `changes_since` response       | <3k tokens for a typical mini-plan delta       |
| Incremental re-index time      | <500ms for 5 changed files in 100-file project |
| Full index time                | <5s for 100-file project                       |
| Cold start (server launch)     | <2s                                            |

**Quality:**

| Metric                  | Target                                         |
| ----------------------- | ---------------------------------------------- |
| Test coverage (Parsers) | 95%+                                           |
| Test coverage (overall) | 90%+                                           |
| SonarAnalyzer warnings  | 0 (clean build)                                |
| TreatWarningsAsErrors   | Enabled, no suppressions without justification |
| Path traversal tests    | Pass all OWASP path traversal test vectors     |
| SQL injection tests     | Pass — zero string-interpolated queries        |
| All tests pass          | 100% green before any merge to main            |

---

## 12. Inspiration & References

- [CodeMunch Pro](https://github.com/BigJai/codemunch-pro) — Python MCP server with tree-sitter, vector embeddings, call graphs. Inspired the architecture; CodeCompress is a lighter .NET alternative with a multi-language extensible parser design.
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) — Official .NET SDK for building MCP servers.
- [TUnit](https://tunit.dev/) — Modern .NET testing framework with source generation and AOT support.
- [tree-sitter-luau](https://github.com/tree-sitter-grammars/tree-sitter-luau) — Luau grammar for tree-sitter. Not used directly (we use regex) but could be a future upgrade path via P/Invoke bindings.
