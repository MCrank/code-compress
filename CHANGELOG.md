# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
