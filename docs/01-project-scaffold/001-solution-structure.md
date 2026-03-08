# 001 — Solution Structure and Project Scaffold

## Summary

Create the foundational project scaffold for CodeCompress, a .NET 10 / C# 14 MCP server that indexes codebases and provides AI agents with compressed, surgical access to code symbols. This plan establishes the solution file, SDK pinning, shared build configuration, central package management, code style enforcement, and the top-level directory layout. Every subsequent plan depends on this one.

## Dependencies

None. This is the first feature in the project.

## Scope

### 1. Solution File

- `CodeCompress.slnx` at the repository root.
- Initially empty (no project references yet — those come in 01-002).

### 2. global.json

- Pin .NET SDK to `10.0.100`.
- Set `rollForward` to `latestFeature` so patch/feature-band updates are picked up automatically while staying on the 10.0.x line.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

### 3. Directory.Build.props

Shared MSBuild properties imported automatically by every project in the repo:

| Property                  | Value          | Rationale                                      |
|---------------------------|----------------|-------------------------------------------------|
| `TargetFramework`         | `net10.0`      | .NET 10 target                                  |
| `LangVersion`             | `14`           | C# 14 language features                         |
| `Nullable`                | `enable`       | Nullable reference types enforced everywhere     |
| `ImplicitUsings`          | `enable`       | Reduce boilerplate `using` directives            |
| `TreatWarningsAsErrors`   | `true`         | Zero-warning policy                              |
| `AnalysisLevel`           | `latest-all`   | Maximum analyzer coverage                        |
| `EnforceCodeStyleInBuild` | `true`         | Style violations break the build                 |

Additionally, include a `PackageReference` to `SonarAnalyzer.CSharp` with `PrivateAssets="all"` so static analysis is applied to every project without repeating the reference.

### 4. Directory.Packages.props (Central Package Management)

Enable `ManagePackageVersionsInternally` and declare all package versions in one place. Individual `.csproj` files will reference packages without version numbers.

| Package                            | Version (latest stable) | Purpose                              |
|------------------------------------|-------------------------|--------------------------------------|
| `ModelContextProtocol`             | 0.2.0-preview.2         | MCP SDK — server hosting, tools      |
| `Microsoft.Data.Sqlite`            | 10.0.0                  | SQLite access with FTS5              |
| `Microsoft.Extensions.Hosting`     | 10.0.0                  | Generic host for DI, logging         |
| `SonarAnalyzer.CSharp`            | 10.6.0.109712           | Static analysis                      |
| `TUnit`                           | 0.15.8                  | Testing framework                    |
| `NSubstitute`                     | 5.3.0                   | Mocking                              |
| `Verify`                          | 28.7.1                  | Snapshot testing                     |

> **Note:** Versions listed above are representative. Resolve to the actual latest stable at time of implementation.

### 5. .editorconfig

Root `.editorconfig` enforcing the project's C# conventions:

- **Naming rules:**
  - PascalCase for public/internal members (methods, properties, classes, etc.)
  - `_camelCase` for private fields (prefixed with underscore)
  - `I` prefix for interfaces
- **Formatting:**
  - Allman (next-line) brace style
  - 4-space indentation, no tabs
- **Code style:**
  - `var` when type is apparent (`csharp_style_var_when_type_is_apparent = true`)
  - Expression-bodied members for single-line implementations
  - `readonly` fields where possible
- **General:**
  - UTF-8, LF line endings, final newline, trim trailing whitespace

### 6. nuget.config

Standard NuGet configuration pointing to the official `nuget.org` feed. Provides a single place to add private feeds later if needed.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

### 7. Directory Structure

Create the following top-level folders (with `.gitkeep` files so they are tracked):

```
src/
tests/
samples/
```

## Acceptance Criteria

- [ ] `global.json` exists and pins SDK 10.0.100 with `rollForward: latestFeature`
- [ ] `CodeCompress.slnx` exists at the repo root
- [ ] `Directory.Build.props` sets all shared properties listed above and references SonarAnalyzer.CSharp
- [ ] `Directory.Packages.props` enables Central Package Management and declares all package versions
- [ ] `.editorconfig` enforces all naming, formatting, and code style rules
- [ ] `nuget.config` exists with the nuget.org feed
- [ ] `src/`, `tests/`, and `samples/` directories exist
- [ ] `dotnet restore` succeeds with zero errors
- [ ] Solution builds with zero warnings (once projects are added in 01-002)

## Files to Create/Modify

| File                        | Action | Notes                                      |
|-----------------------------|--------|--------------------------------------------|
| `CodeCompress.slnx`          | Create | Empty solution                              |
| `global.json`               | Create | SDK pin                                     |
| `Directory.Build.props`     | Create | Shared build properties + SonarAnalyzer     |
| `Directory.Packages.props`  | Create | Central Package Management versions         |
| `.editorconfig`             | Create | Code style enforcement                      |
| `nuget.config`              | Create | NuGet feed configuration                    |
| `src/.gitkeep`              | Create | Ensure directory is tracked                 |
| `tests/.gitkeep`            | Create | Ensure directory is tracked                 |
| `samples/.gitkeep`          | Create | Ensure directory is tracked                 |

## Out of Scope

- Individual `.csproj` files (covered in 01-002)
- Source code files (covered in later plans)
- CI/CD pipeline configuration
- Docker / container setup
- LICENSE or README files

## Notes / Decisions

1. **Central Package Management** — All NuGet versions live in `Directory.Packages.props`. This avoids version drift across projects and makes upgrades atomic.
2. **SonarAnalyzer in Directory.Build.props** — Referencing the analyzer at the repo level ensures every project gets static analysis without opt-in. The `PrivateAssets="all"` attribute prevents it from flowing to consumers.
3. **rollForward: latestFeature** — Chosen over `latestPatch` to allow feature-band SDK updates (e.g., 10.0.200) while still requiring the 10.0.x major/minor line.
4. **.editorconfig as source of truth** — The editor config enforces style in IDEs and via `EnforceCodeStyleInBuild` during CI builds, ensuring consistent formatting regardless of developer tooling.
5. **Versions are advisory** — The version numbers in the table above should be verified against nuget.org at implementation time and updated to the actual latest stable releases.
