# 002 — Project Files (.csproj)

## Summary

Create all `.csproj` project files for the CodeCompress solution and register them in `CodeCompress.slnx`. This establishes the three source projects (Core library, MCP Server executable, CLI executable) and three test projects (Core tests, Server tests, Integration tests). All projects use Central Package Management — no version numbers appear in any `.csproj` file.

## Dependencies

| Plan   | Description              | Reason                                                        |
|--------|--------------------------|---------------------------------------------------------------|
| 01-001 | Solution Structure       | Solution file, Directory.Build.props, Directory.Packages.props, and folder layout must exist before projects can be created |

## Scope

### Source Projects

#### 1. `src/CodeCompress.Core/CodeCompress.Core.csproj`

- **Output type:** Class library (default)
- **Purpose:** Core library containing parsers, indexing engine, SQLite storage, models, and path validation
- **Package references:**
  - `Microsoft.Data.Sqlite` — SQLite access with FTS5 support
  - `Microsoft.Extensions.Hosting` — DI abstractions (`IServiceCollection`, `IHostBuilder`, etc.)

#### 2. `src/CodeCompress.Server/CodeCompress.Server.csproj`

- **Output type:** `Exe`
- **Purpose:** MCP server executable with stdio transport and `[McpServerToolType]` tool classes
- **Project references:**
  - `CodeCompress.Core`
- **Package references:**
  - `ModelContextProtocol` — MCP SDK for server hosting and tool registration
  - `Microsoft.Extensions.Hosting` — Generic host for DI, logging, lifetime management

#### 3. `src/CodeCompress.Cli/CodeCompress.Cli.csproj`

- **Output type:** `Exe`
- **Purpose:** Optional standalone CLI for testing and debugging index operations outside the MCP protocol
- **Project references:**
  - `CodeCompress.Core`

### Test Projects

All test projects set `<IsTestProject>true</IsTestProject>` to enable test discovery and ensure correct build behavior.

#### 4. `tests/CodeCompress.Core.Tests/CodeCompress.Core.Tests.csproj`

- **Output type:** `Exe` (required by TUnit's source-generated test runner)
- **Purpose:** Unit tests for all Core library components (parsers, storage, index engine, models, validation)
- **Project references:**
  - `CodeCompress.Core`
- **Package references:**
  - `TUnit` — Testing framework (source-generated, AOT-compatible)
  - `NSubstitute` — Interface mocking
  - `Verify` — Snapshot testing for complex outputs

#### 5. `tests/CodeCompress.Server.Tests/CodeCompress.Server.Tests.csproj`

- **Output type:** `Exe` (required by TUnit)
- **Purpose:** Unit tests for MCP tool classes and server-specific logic
- **Project references:**
  - `CodeCompress.Server`
  - `CodeCompress.Core`
- **Package references:**
  - `TUnit` — Testing framework
  - `NSubstitute` — Interface mocking

#### 6. `tests/CodeCompress.Integration.Tests/CodeCompress.Integration.Tests.csproj`

- **Output type:** `Exe` (required by TUnit)
- **Purpose:** End-to-end integration tests exercising the full pipeline (parse, index, query)
- **Project references:**
  - `CodeCompress.Core`
- **Package references:**
  - `TUnit` — Testing framework

### Solution Registration

All six projects must be added to `CodeCompress.slnx` with appropriate solution folders:

```
CodeCompress.slnx
  src/
    CodeCompress.Core
    CodeCompress.Server
    CodeCompress.Cli
  tests/
    CodeCompress.Core.Tests
    CodeCompress.Server.Tests
    CodeCompress.Integration.Tests
```

### Placeholder Files

Each project needs a minimal placeholder so it compiles:
- **Libraries / Executables:** An empty `Program.cs` (for Exe projects) or a placeholder class file
- **Test projects:** An empty test file or just the project file (TUnit does not require a `Main` method; the source generator provides it)

## Acceptance Criteria

- [ ] All six `.csproj` files exist in their respective directories
- [ ] No `.csproj` file contains any `<PackageVersion>` or inline version attribute — all versions come from `Directory.Packages.props`
- [ ] `CodeCompress.Core.csproj` references `Microsoft.Data.Sqlite` and `Microsoft.Extensions.Hosting`
- [ ] `CodeCompress.Server.csproj` references `CodeCompress.Core` project, `ModelContextProtocol`, and `Microsoft.Extensions.Hosting`
- [ ] `CodeCompress.Cli.csproj` references `CodeCompress.Core` project
- [ ] `CodeCompress.Core.Tests.csproj` references `CodeCompress.Core` project, `TUnit`, `NSubstitute`, and `Verify`
- [ ] `CodeCompress.Server.Tests.csproj` references `CodeCompress.Server` project, `CodeCompress.Core` project, `TUnit`, and `NSubstitute`
- [ ] `CodeCompress.Integration.Tests.csproj` references `CodeCompress.Core` project and `TUnit`
- [ ] All test projects have `<IsTestProject>true</IsTestProject>`
- [ ] All six projects are registered in `CodeCompress.slnx` under the correct solution folders
- [ ] `dotnet restore CodeCompress.slnx` succeeds with zero errors
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings
- [ ] `dotnet test CodeCompress.slnx` runs successfully (even with no tests defined yet)

## Files to Create/Modify

| File                                                             | Action | Notes                                        |
|------------------------------------------------------------------|--------|----------------------------------------------|
| `src/CodeCompress.Core/CodeCompress.Core.csproj`                 | Create | Class library with Sqlite + Hosting refs     |
| `src/CodeCompress.Server/CodeCompress.Server.csproj`             | Create | Exe with Core project ref + MCP + Hosting    |
| `src/CodeCompress.Cli/CodeCompress.Cli.csproj`                   | Create | Exe with Core project ref                    |
| `tests/CodeCompress.Core.Tests/CodeCompress.Core.Tests.csproj`   | Create | Exe, IsTestProject, TUnit + NSubstitute + Verify |
| `tests/CodeCompress.Server.Tests/CodeCompress.Server.Tests.csproj` | Create | Exe, IsTestProject, TUnit + NSubstitute     |
| `tests/CodeCompress.Integration.Tests/CodeCompress.Integration.Tests.csproj` | Create | Exe, IsTestProject, TUnit              |
| `CodeCompress.slnx`                                               | Modify | Add all six projects with solution folders   |
| Placeholder `.cs` files per project                              | Create | Minimal files so projects compile            |

## Out of Scope

- Actual source code implementation (parsers, storage, tools, etc.)
- Test method implementations
- Build scripts or CI/CD configuration
- NuGet package publishing configuration
- Any changes to `Directory.Build.props` or `Directory.Packages.props` (established in 01-001)

## Notes / Decisions

1. **TUnit requires Exe output type** — Unlike xUnit/NUnit, TUnit uses source generation to produce its own entry point. All test projects must be `OutputType: Exe` for the generated test runner to work correctly.
2. **IsTestProject property** — Setting `<IsTestProject>true</IsTestProject>` ensures `dotnet test` discovers and runs the projects, and prevents them from being published or packed accidentally.
3. **Server references both MCP and Hosting** — The Server project needs its own `Microsoft.Extensions.Hosting` reference for `GenericHost` integration, separate from Core's use of DI abstractions.
4. **Integration tests reference only Core** — Integration tests exercise the full pipeline through the public Core API, not through MCP tool classes. This keeps them transport-agnostic.
5. **No version numbers in .csproj** — Central Package Management (enabled in 01-001) requires that all versions are declared exclusively in `Directory.Packages.props`. Any inline version in a `.csproj` will cause a build error.
6. **Solution folders** — Using `src/` and `tests/` solution folders in the `.slnx` file mirrors the physical directory layout and keeps the solution explorer organized.
