# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Tool annotations** — all 22 tools now declare accurate `ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld`/`Title` hints instead of the MCP SDK's `Destructive=true`/`OpenWorld=true` defaults, so hosts no longer prompt for confirmation before harmless read-only queries (#207)
- **Cache hints (SEP-2549)** — `tools/list` and `prompts/list` advertise a 1-hour `Private`-scope TTL and return both listings in deterministic (alphabetical) order across server processes, so clients can cache them instead of re-fetching on every connection (#208)
- **Structured content & output schemas** — the 15 tools that return JSON now declare `UseStructuredContent = true` with a typed, schema-validated response (`CodeCompress.Core/Contracts`) instead of a hand-serialized JSON string; the CLI's `--json` output reuses the same record types so it cannot drift from `structuredContent` (#209)
- **`index_project` as an MCP task** — supports the Tasks extension (`io.modelcontextprotocol/tasks`, SEP-2663) as `ToolTaskSupport.Optional`: a client that declares the extension and invokes it via the task-aware call path gets a task handle back immediately instead of blocking for the 5–120s a first-time index can take, then polls to completion. Clients that don't use the extension are unaffected — an ordinary call still blocks and returns the result directly (#210)

### Changed
- **BREAKING:** `ModelContextProtocol` bumped from 1.4.0 to 2.2.0, adopting MCP specification revision **2026-07-28**. Client support for protocol revisions prior to 2025-11-25 is dropped. CodeCompress remains stdio-only, so the stateless-HTTP-oriented breaking changes in this spec revision (`Mcp-Session-Id` removal, `subscriptions/listen`, SSE resumability removal, OAuth hardening) do not affect it; no Roots, Sampling, or MCP Logging APIs were in use, so none of the SEP-2577 deprecations apply (#206)
- **Package bump** — `Microsoft.Data.Sqlite` 10.0.8 → 10.0.11, resolving a high-severity transitive advisory in `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (GHSA-2m69-gcr7-jv3q) that otherwise fails the `TreatWarningsAsErrors` build (#206)
- **`find_unused_symbols`/`list_repos`** now return `{results: [...]}`/`{repos: [...]}` instead of a bare JSON array, avoiding an MCP SDK legacy-wire array-wrapping inconsistency across client protocol versions; the CLI's `unused-symbols`/`list`/`find-references` commands match (#209)
- **CLI `get-symbol --json`** now returns actual `source_code` (previously it only returned the raw internal DB record with no source at all) — matches the MCP `get_symbol` tool; `blast-radius` now reports `NOT_FOUND` instead of silently showing "0 files affected" for a nonexistent file/symbol (#209)

## [0.15.0] - 2026-06-10

### Added
- **Ruby parser** — indexes Ruby files as queryable symbols: classes, modules, instance/class/singleton methods, constants, attribute accessors (`attr_reader`/`attr_writer`/`attr_accessor`), and module includes (#202)
- **Access boundary** — the MCP server is now clamped to the directory it was launched from (and its descendants), preventing an agent from indexing, querying, or enumerating repositories outside its working directory. The boundary root defaults to the launch working directory and can be overridden with the `CODECOMPRESS_ROOT` environment variable. An optional `CODECOMPRESS_ALLOWED_ROOTS` allowlist (delimited by the OS path separator) grants additional trusted roots for multi-repo workflows; filesystem-root entries are rejected as too broad. `list_repos` now returns only repositories within the boundary, and out-of-bounds requests are rejected with a uniform `INVALID_PATH` error that leaks no information about the requested path (#198)
- **Global single index database** — all indexed projects share one SQLite database at `~/.code-compress/index.db`. A repository registry tracks file/symbol counts and last-indexed timestamps, exposed via `list_repos` (#184)
- **MCP prompts for progressive tool disclosure** — four named workflows (`ExploreCodebase`, `FindImpact`, `ReviewChanges`, `DebugSymbol`) guide agents through optimal multi-tool sequences, reducing the cognitive load of discovering the right tool combination from 22 tools (#181)
- **`get_hot_path` tool** — extracts only the lines within a symbol that contain specific identifiers, with configurable surrounding context lines (~10–40× token savings vs loading the full symbol body) (#180)
- **Typed dependency edges** — `dependency_graph` edges now carry a typed `edge_kind` value (`imports`, `calls`, `implements`, `inherits`, `references`) for more precise relationship queries (#178)
- **`blast_radius` tool** — reverse BFS starting from a symbol or file, returning all files and symbols that would break if it changed (#178)
- **`find_unused_symbols` tool** — detects public symbols with no incoming dependency edges across the indexed project (#178)
- **PascalCase/camelCase-aware search** — `search_symbols` now auto-splits compound identifiers (e.g., `getUserById` → `get`, `user`, `by`, `id`) and falls back to fuzzy matching when exact FTS5 results are empty (#179)

### Changed
- **BREAKING:** Tools no longer accept arbitrary absolute paths. A `path` argument that resolves outside the boundary root (launch working directory or `CODECOMPRESS_ROOT`) is rejected. Workflows that previously passed paths to unrelated repositories must launch the server from a common parent directory or configure `CODECOMPRESS_ROOT` / `CODECOMPRESS_ALLOWED_ROOTS` (#198)
- **JSON config parser** migrated from regex to tree-sitter AST queries for accurate key/value extraction including unicode keys, nested objects, and arrays (#202)
- **All regex-based parsers** (C#, Java, Go, TypeScript/JavaScript, Rust, Python) replaced with tree-sitter AST queries for more reliable symbol extraction across edge cases (#186)
- **Web dashboard on hold** — `CodeCompress.Web` and `CodeCompress.Web.Tests` are excluded from the solution build. The `codecompress web` CLI subcommand is removed. Project files are preserved in the repo for future resumption (#200)
- **Package bumps** — ModelContextProtocol 1.4.0, Microsoft.Data.Sqlite / Extensions.* 10.0.8, System.CommandLine 2.0.8, YamlDotNet 18.0.0, SonarAnalyzer.CSharp 10.27.0, TUnit 1.49.0, Verify 31.19.0

## [0.14.0] - 2026-04-11

### Added
- `pathFilter` parameter on `assemble_context` — scopes assembled context to a specific directory prefix (e.g., `src/`), matching the existing pattern from `search_symbols` and `project_outline`. Applies to both MCP Server and CLI (#169)
- `.gitignore` support during file discovery — `IndexEngine` now automatically excludes files matching `.gitignore` rules via `git check-ignore`. Hardcoded exclusions remain as baseline. Falls back gracefully when git is not installed or directory is not a git repo (#170)

### Fixed
- `assemble_context` now catches FTS5 query errors instead of returning a generic "An error occurred" message — retries with literal phrase, returns structured `FTS5_QUERY_ERROR` JSON on failure. Same fix applied to CLI `search-symbols` and `assemble` commands (#168)
- `ValidatePathFilter` now LIKE-escapes `%`, `_`, and `!` characters instead of stripping them, fixing corruption of legitimate paths containing underscores (e.g., `src/my_module/` was silently mangled to `src/mymodule/`). Also adds missing `ESCAPE '!'` clause to `GetProjectOutlineAsync` (#173)

## [0.13.0] - 2026-03-25

### Added
- **YAML config parser** — indexes `.yaml`/`.yml` files as queryable `ConfigKey` symbols with colon-delimited hierarchical qualified names, hybrid array handling (object arrays indexed individually, scalar arrays summarized), and security hardening against YAML bombs, prompt injection, and stack overflow (#162)
- YAML sample project with Kubernetes settings, Docker Compose, and i18n configuration files (#162)

### Fixed
- Java, Go, Rust, Python, and TypeScript/JavaScript parsers now registered in DI container — previously implemented but unreachable at runtime, causing `index_project` to silently skip files for these languages (#161)

### Changed
- NuGet packages now ship as platform-specific packages (~2.5 MB each) instead of a single monolithic package (~17.5 MB), reducing end-user download size by ~85%. Supported platforms: win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64, osx-x64 (#164)

## [0.12.0] - 2026-03-25

### Added
- `expand_symbol` prefix matching — when a qualified name like `Parent:ChildPrefix` doesn't exactly match, tries prefix matching within the parent scope before falling back to unscoped search. Single match auto-resolves; multiple matches return candidate list with full qualified names (#155)
- `GetSymbolsByParentAndChildPrefixAsync` store method for parent-scoped prefix lookups (#155)

### Fixed
- `assemble_context` now returns results for multi-word natural-language queries — tokenizes input, strips stopwords, joins terms with OR instead of implicit AND that returned 0 results (#154)
- MIXED_PATTERN error now includes a `suggestions` array with ready-to-use query strings and directive wording that agents will follow (#156)

### Changed
- `search_symbols` description now explicitly directs agents to `search_text` for content/pattern searches (#156)
- `search_text` description leads with concrete use cases (string literals, comments, TODOs, audit patterns like `FromSqlRaw`) (#156)
- CLI `assemble` and `expand-symbol` commands updated for feature parity with MCP server tools (#154, #155)

## [0.11.0] - 2026-03-23

### Added
- **Java language parser** — classes, interfaces, enums, records, annotation types, methods, inner classes, Javadoc, generics (#142)
- **Go language parser** — structs, interfaces (generic), functions, receiver methods, constants, visibility by capitalization (#140)
- **TypeScript/JavaScript parser** — single parser for .ts/.tsx/.js/.jsx/.mjs/.cjs; classes, interfaces, enums, type aliases, arrow functions, ESM/CJS imports, JSDoc (#138)
- **Rust language parser** — structs, enums, traits, impl block methods, macro_rules!, type aliases, derive attributes, /// doc comments (#141)
- **Python language parser** — first indentation-based parser; classes, functions, methods, decorators, constants, .py/.pyi support (#137)
- **`assemble_context` MCP tool** — one-shot context assembly within a token budget; combines search + source retrieval + file overview in a single call, reducing 5-10 round-trips to 1 (#139)
- **CLI `assemble` command** — CLI equivalent of assemble_context (#139)
- FTS5 index now includes `parent_symbol` — searching "ClassName MethodName" finds methods by parent type (#143)

### Fixed
- `get_symbol` with unqualified name now prefers class over constructor when both share the same name (#144)

### Changed
- README comprehensively updated with all MCP tools, CLI commands, new languages, and agent configuration (#139)

## [0.10.0] - 2026-03-20

### Added
- Auto contains-match fallback: `search_symbols` automatically retries with `*query*` when a plain term returns zero FTS5 results, improving symbol discoverability without manual wildcards (e.g., "Validator" now finds "PathValidator", "IPathValidator") (#132)
- `GlobPattern.IsPlainTerm()` helper for detecting fallback-eligible queries (#132)
- `fallback_used` response field indicates when contains-matching was used (#132)
- Stale index hint in `SYMBOL_NOT_FOUND` error guidance — suggests re-running `index_project` when a symbol may exist but the index is outdated (#133)

### Changed
- `search_symbols` tool description updated to document auto-fallback behavior (#132)

## [0.9.0] - 2026-03-20

### Added
- Error classification: all error responses include `retryable` field for programmatic error handling (#109)
- Fuzzy symbol resolution: `get_symbol` and `expand_symbol` accept unqualified names and auto-resolve unique matches, returning candidates list on ambiguity (#111, #120)
- Next-action hints in MCP tool responses (#113) and CLI output (#123)
- Structured JSON error output in CLI `--json` mode with error codes matching MCP server format (#119)
- `GetSymbolCandidatesByNameAsync` store method for unqualified symbol name lookup (#111)
- `PathValidator.NormalizeRelativePath()` for backslash-to-forward-slash normalization (#112)

### Changed
- MCP tool descriptions: output schemas (#102), error codes (#103), parameter constraints (#101), performance hints (#110), glob examples & cross-references (#104)
- CLI `agent-instructions` command: JSON output schemas, error code reference, performance tips, parameter constraints (#122)
- CLI help text: parameter ranges, defaults, clamping behavior, enum allowed values (#124)
- Path normalization for `modulePath` and `rootFile` in MCP and CLI (#112, #121)
- `implement-plan` skill: `stop_server` guidance for build file locks

### Fixed
- Terraform sample `modules.tf` cloudwatch module source path corrected
- CLI `search-text` empty query error now correctly sets exit code 1 (#119)

## [0.8.0] - 2026-03-17

### Added
- C# record and class primary constructor parameters indexed as individual child symbols (`SymbolKind.Constant`), making them independently searchable via FTS5 and expandable via qualified name e.g. `expand_symbol("Order:Id")` (#96)
- JSON Config sample project created from scratch with integration tests covering all value types, nested sections, and UTF-8 multi-byte characters (#97)
- COVERAGE.md added to each of the 6 sample directories documenting exercised parser constructs (#97)
- 15 new unit tests for record/class parameter extraction and 12 new JSON Config integration tests (#96, #97)

### Changed
- All 6 language sample projects expanded to 90%+ parser construct coverage (#97)
- C# sample: added struct, record struct, sealed class, partial record, class primary constructor, operators, indexer, finalizer, virtual/override, file-scoped type, block-scoped namespace (#97)
- Luau sample: added while/do, repeat/until, do/end blocks, nested local functions (#97)
- Blazor Razor sample: added @using alias, multiple @code blocks, empty @code block (#97)
- .NET Project sample: added multi-target project, nested Version element, AssemblyName (#97)
- `/implement-plan` skill Step 6 updated with guidance for updating existing sample projects on parser enhancements (#97)

### Fixed
- Terraform sample `modules.tf` invalid `dashboard_name` attribute replaced with valid CloudWatch alarm attributes (#97)

## [0.7.0] - 2026-03-17

### Added
- Size guard on `get_symbol`: large symbols (>16KB) with children return a guided summary with child method signatures and `expand_symbol` instructions instead of full source. Use `force=true` to bypass (#93)
- Kind-based ranking boost for symbol search: structural types (Class, Interface, Record) rank above members (Method, Function) above config keys. SQL LIKE path now has deterministic ordering (#92)
- Structured error handling with diagnostic file logging to `.code-compress/codecompress-YYYY-MM-DD.log` with 10-file retention and copy-paste GitHub bug report template (#94)
- Compound FTS5 queries (`Claude* OR Agent*`) now route correctly to FTS5 instead of SQL LIKE. Mixed-strategy queries return LLM-friendly error with workaround suggestion (#89)
- `total_files` and `files_errored` fields in index output for clearer incremental indexing and parse failure visibility (#88, #94)
- `guidance` field added to all MCP tool error responses for actionable agent-facing messages (#94)
- `Fts5QuerySanitizer` moved from Server to Core for shared access by MCP server and CLI (#90)

### Fixed
- JSON config parser crashes on files with multi-byte UTF-8 characters (accented names, emoji, CJK) due to byte vs char offset confusion (#91)
- CLI `search-text` crashes on queries with FTS5 special characters (dots, colons, parentheses) — now sanitized with try/catch fallback (#90)
- Compound FTS5 prefix wildcard queries (`Claude* OR Agent*`) returning zero results — misrouted to SQL LIKE where OR was treated as literal text (#89)
- `files_skipped` renamed to `files_unchanged` in index output to prevent AI agents from misinterpreting healthy incremental behavior as failures (#88)
- NuGet README rendering: switched to markdown image syntax for compatibility (#80)

### Changed
- Skill delegation documentation clarified in CLAUDE.md and implement-plan skill — explicit instructions for reading SKILL.md files and inlining into agent prompts (#88)

## [0.6.0] - 2026-03-14

### Added
- CLI modernization: migrate to System.CommandLine with `--json` global flag, `--version`, workflow-guided `--help`, and `agent-instructions` command (#74)
- 7 new CLI commands for full MCP server feature parity: `invalidate-cache`, `get-module-api`, `expand-symbol`, `get-symbols`, `topic-outline`, `project-deps`, `find-references` (#75)
- Full parameter parity on existing CLI commands: `outline`, `search`, `search-text`, `deps` now match all MCP tool parameters (#76)
- MCP server identity (`ServerInfo`) and workflow instructions sent to agents on connect (#73)
- Enhanced all 17 MCP tool descriptions with efficiency messaging and prerequisites (#73)
- Dynamic version from `AssemblyInformationalVersionAttribute` — no manual version updates (#73)
- Project-level `/create-release` skill with mandatory `Directory.Build.props` version bump (#73)
- Specialized Claude Code skills: `tdd-expert`, `security-expert`, `cli-expert`, `parser-expert` with shared `dotnet-reference.md` knowledge base (#70)
- Auto-detect git project root for `.code-compress` database location — subfolder paths resolve to nearest `.git` directory (#77)

### Changed
- README: CLI positioned as first-class alongside MCP server, Cursor/Windsurf install sections, agent configuration block, package icon, full GitHub raw URL for banner (#78)
- `.code-compress/` directories fully gitignored (#72)
- CLAUDE.md updated with skills documentation and mandatory security review (#72)

### Fixed
- Cursor install documentation now uses correct `.cursor/mcp.json` path (#78)

### Removed
- Idle timeout auto-shutdown feature — server now runs indefinitely until manually stopped (#71)
- `IActivityTracker`, `ActivityTracker`, `IdleTimeoutService`, `IdleTimeoutOptions` and all `RecordActivity()` calls (#71)
- `--idle-timeout` CLI argument and `CODECOMPRESS_IDLE_TIMEOUT` environment variable (#71)

## [0.5.0] - 2026-03-13

### Added
- Blazor Razor parser for `.razor` file support (#58)
- Terraform parser for `.tf` and `.tfvars` file indexing (#59)
- Sample projects and integration tests for Blazor and Terraform parsers (#59)

### Changed
- Updated README with all available parsers and Terraform language support (#58, #59)

## [0.4.0] - 2026-03-12

### Added
- Topic-scoped outline tool (`topic_outline`) for cross-project topic queries (#54)
- JSON configuration file parser (`JsonConfigParser`) for indexing JSON config files (#53)
- Cross-project dependency graph tool (`project_dependencies`) (#52)
- `find_references` tool for symbol usage search across indexed projects (#50)
- `expand_symbol` tool for targeted nested symbol extraction (#49)
- Pagination support for `project_outline` to prevent MCP response overflow (#47)
- Improved `pathFilter` discoverability in search_symbols, search_text, and project_outline tool descriptions (#51)

### Fixed
- `SymbolKind.Enum` added so C# enums are discoverable by kind (#48)
- `SymbolKind.Record` added so C# records are discoverable by name and kind (#46)
- Cross-platform consistency for context test newlines (#49)

### Changed
- CI workflows updated — removed redundant triggers, bumped actions to v5, upload-artifact to v6 (#36)
- Fixed outdated database location references in README and CLAUDE.md (#36)

## [0.3.0] - 2026-03-12

### Added
- Server lifecycle management with stop tool and idle timeout (#34)
- .NET project parser (DotNetProjectParser) for MSBuild project files (#27)
- FTS5 glob matching and file path filter for search tools (#26)
- pathFilter parameter for project_outline tool (#25)

### Fixed
- search_symbols rejecting wildcard queries when pathFilter is provided (#33)
- project_outline pathFilter broken on Windows (#31)
- Kind filter case-sensitivity and C# parser gaps (#24)
- Cross-platform ProjectReference name extraction (#27)
