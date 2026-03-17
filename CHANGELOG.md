# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
