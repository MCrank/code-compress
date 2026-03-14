# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
