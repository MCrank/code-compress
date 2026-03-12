# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
