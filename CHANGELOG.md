# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
