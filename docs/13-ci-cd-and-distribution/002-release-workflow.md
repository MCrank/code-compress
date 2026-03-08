# 002 — Release Workflow and dotnet Tool Packaging

## Summary

Automated release pipeline triggered by version tags that builds, tests, packs the CLI as a `dotnet tool`, and publishes to NuGet. Also covers the CLI project's packaging configuration and a basic command-line interface that exposes the same functionality as the MCP server tools via console commands.

## Dependencies

- **Feature 07** — MCP server host and DI registration pattern (reused by CLI).
- **Feature 13-001** — CI workflow (release workflow extends the CI pattern).
- **All core features** — The CLI exercises all core functionality.

## Scope

### 1. Release Workflow (`.github/workflows/release.yml`)

| Aspect | Detail |
|---|---|
| Trigger | `push` with tag matching `v*` (e.g., `v1.0.0`, `v0.1.0-preview.1`) |
| Runner | `ubuntu-latest` (single platform — the package is platform-independent) |
| .NET SDK | `10.0.x` |

### 2. Release Workflow Steps

| Step | Command/Action | Notes |
|---|---|---|
| Checkout | `actions/checkout@v4` | |
| Setup .NET | `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` | |
| Restore | `dotnet restore CodeCompress.slnx` | |
| Build | `dotnet build CodeCompress.slnx --no-restore -c Release` | Zero warnings enforced |
| Test | `dotnet test CodeCompress.slnx --no-build -c Release` | All tests must pass before publishing |
| Determine version | Extract version from tag: `echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_ENV` | Tag `v1.0.0` → version `1.0.0` |
| Pack | `dotnet pack src/CodeCompress.Cli/CodeCompress.Cli.csproj -c Release -o ./nupkg /p:Version=$VERSION` | Produces `.nupkg` |
| Publish to NuGet | `dotnet nuget push ./nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json` | Requires `NUGET_API_KEY` secret |
| Create GitHub Release | `gh release create ${{ github.ref_name }} ./nupkg/*.nupkg --generate-notes` | Attaches nupkg as release asset |

### 3. CLI Project Packaging (`src/CodeCompress.Cli/CodeCompress.Cli.csproj`)

Add the following properties to enable `dotnet tool` packaging:

| Property | Value | Purpose |
|---|---|---|
| `PackAsTool` | `true` | Marks project as a dotnet tool |
| `ToolCommandName` | `codecompress` | CLI command name after `dotnet tool install` |
| `PackageId` | `CodeCompress` | NuGet package identifier |
| `Description` | `CLI tool for indexing codebases and querying code symbols with compressed, token-efficient output` | NuGet description |
| `Authors` | Project authors | |
| `License` | `MIT` | Matches repository license |
| `PackageProjectUrl` | Repository URL | |
| `RepositoryUrl` | Repository URL | |
| `PackageReadmeFile` | `README.md` (if included in package) | |
| `PackageTags` | `code-index;mcp;ai;codebase;symbols` | Discoverability |

### 4. CLI Program.cs (`src/CodeCompress.Cli/Program.cs`)

A command-line interface that exercises the same core services as the MCP server but with console output instead of MCP protocol. Uses the same DI setup.

| Command | Description | Maps to MCP Tool |
|---|---|---|
| `codecompress index <path>` | Index a project directory | `index_project` |
| `codecompress outline <path>` | Show project outline | `project_outline` |
| `codecompress get-symbol <path> <name>` | Retrieve a specific symbol | `get_symbol` |
| `codecompress search <path> <query>` | Search symbols by name | `search_symbols` |
| `codecompress search-text <path> <query>` | Full-text search in files | `search_text` |
| `codecompress changes <path> <label>` | Show changes since snapshot | `changes_since` |
| `codecompress snapshot <path> [label]` | Create a snapshot | `snapshot_create` |
| `codecompress file-tree <path>` | Show annotated file tree | `file_tree` |
| `codecompress deps <path> [file]` | Show dependency graph | `dependency_graph` |

Command-line parsing: Use `System.CommandLine` library or simple manual `args` parsing. Each command creates a service collection, resolves the needed services, calls the core logic, and prints results to stdout.

### 5. DI Setup for CLI

Reuse the same service registrations as the MCP server (extract a shared `ServiceRegistration` class or extension method):

```csharp
services.AddSingleton<ILanguageParser, LuauParser>();
services.AddSingleton<ILanguageParser, CSharpParser>();
services.AddSingleton<IndexEngine>();
services.AddSingleton<SymbolStore>();
services.AddSingleton<SqliteConnectionFactory>();
services.AddSingleton<FileHasher>();
services.AddSingleton<ChangeTracker>();
services.AddSingleton<PathValidator>();
```

### 6. Tests

| Test | Description | Location |
|---|---|---|
| CliPackage_HasCorrectMetadata | Verify `.csproj` has `PackAsTool`, `ToolCommandName`, required metadata | `tests/CodeCompress.Cli.Tests/` or manual |
| CliBuilds_InReleaseConfiguration | `dotnet build -c Release` succeeds for CLI project | CI workflow |
| CliPack_ProducesNupkg | `dotnet pack` produces a valid `.nupkg` | CI workflow or test |
| ReleaseWorkflow_YamlValid | Release workflow YAML is syntactically valid | Manual or actionlint |

## Acceptance Criteria

- [ ] `.github/workflows/release.yml` exists and is valid GitHub Actions YAML.
- [ ] Release workflow triggers on `v*` tag push.
- [ ] Workflow runs build and tests before packing/publishing.
- [ ] Version is extracted from the git tag and applied to the package.
- [ ] `dotnet pack` produces a valid `.nupkg` for the CLI tool.
- [ ] Package is published to NuGet via `dotnet nuget push` with API key secret.
- [ ] GitHub Release is created with the nupkg attached.
- [ ] `CodeCompress.Cli.csproj` has `PackAsTool=true` and `ToolCommandName=codecompress`.
- [ ] Package metadata (description, license, tags, URLs) is populated.
- [ ] CLI `Program.cs` provides commands for all major operations (index, outline, get-symbol, search, changes, snapshot, file-tree, deps).
- [ ] CLI reuses the same DI service registrations as the MCP server.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] `dotnet tool install --global` works from the produced nupkg (manual verification).

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `.github/workflows/release.yml` | GitHub Actions release workflow |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Cli/CodeCompress.Cli.csproj` | Add `PackAsTool`, `ToolCommandName`, package metadata properties |
| `src/CodeCompress.Cli/Program.cs` | Implement CLI commands with DI and console output |
| `src/CodeCompress.Server/Program.cs` | Extract shared service registration to a common method (optional — could also duplicate) |

## Out of Scope

- Publishing to a private NuGet feed — public NuGet.org only.
- Homebrew, APT, or Chocolatey packages — NuGet dotnet tool only for MVP.
- Auto-versioning from commit history (GitVersion, Nerdbank.GitVersioning) — version comes from the tag.
- Signed NuGet packages — not required for MVP.
- Platform-specific native builds (self-contained, trimmed, AOT) — the dotnet tool requires the .NET runtime.
- Changelog generation — GitHub's `--generate-notes` provides basic release notes.

## Notes / Decisions

1. **Shared service registration.** Extracting DI registrations into a shared extension method (`IServiceCollection.AddCodeCompressCore()`) avoids duplication between `Server/Program.cs` and `Cli/Program.cs`. This could live in `CodeCompress.Core` as an extension. However, if the CLI's needs diverge (e.g., different logging), keeping them separate is also acceptable.
2. **System.CommandLine vs. manual parsing.** `System.CommandLine` provides argument parsing, help text, and tab completion for free. However, it adds a dependency. For MVP, simple manual `args` parsing with a `switch` statement is sufficient. `System.CommandLine` can be adopted later if the CLI grows.
3. **NuGet API key security.** The `NUGET_API_KEY` must be stored as a GitHub Actions secret. The release workflow uses `${{ secrets.NUGET_API_KEY }}` — it is never logged or exposed.
4. **Tag-based versioning.** The tag `v1.0.0` becomes version `1.0.0` in the package. Pre-release versions use tags like `v0.1.0-preview.1`. This is simple and explicit — no magic versioning tools needed.
5. **Single-platform release.** The dotnet tool is platform-independent (it is a NuGet package containing managed assemblies). Building on `ubuntu-latest` only is sufficient for the release workflow. The CI workflow already verifies cross-platform compatibility.
