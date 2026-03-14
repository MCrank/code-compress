# .NET 10 / C# 14 Project Reference — CodeCompress

This is the shared .NET knowledge base for all CodeCompress skills and agents. When writing or reviewing code for this project, follow these conventions exactly.

## Platform & Tooling

- **.NET 10** (SDK 10.0.100, pinned via `global.json` with `rollForward: latestFeature`)
- **C# 14** (`LangVersion 14` in `Directory.Build.props`)
- **Target Framework:** `net10.0`
- **Nullable Reference Types:** Enabled globally (`<Nullable>enable</Nullable>`)
- **Treat Warnings As Errors:** `true` — zero warnings allowed
- **Analysis Level:** `latest-all` with `EnforceCodeStyleInBuild true`
- **Static Analysis:** SonarAnalyzer.CSharp enforced at build time

## Project Structure

```
src/
  CodeCompress.Core/        — Core library: parsers, indexing, storage, models, validation
  CodeCompress.Server/      — MCP server: tool classes, DI, stdio transport
  CodeCompress.Cli/         — CLI tool: commands, output formatting
tests/
  CodeCompress.Core.Tests/  — Unit tests mirroring Core
  CodeCompress.Server.Tests/ — Unit tests mirroring Server
  CodeCompress.Integration.Tests/ — End-to-end integration tests
samples/
  csharp-sample-project/    — Realistic C# files for parser testing
  luau-sample-project/      — Realistic Luau files for parser testing
  terraform-sample-project/ — Realistic Terraform files for parser testing
```

## Build System

- **Central Package Management:** All NuGet versions in `Directory.Packages.props` — `.csproj` files omit version numbers
- **Shared Build Settings:** `Directory.Build.props` — shared across all projects
- **Solution File:** `CodeCompress.slnx` (XML format, .NET 10 native)

**Commands:**
```bash
dotnet build CodeCompress.slnx          # Build all — must produce zero warnings
dotnet test CodeCompress.slnx           # Run all tests
dotnet test --filter "FullyQualifiedName~ClassName"  # Run specific tests
```

## Naming Conventions (Enforced via .editorconfig)

| Element | Convention | Example |
|---------|-----------|---------|
| Public/internal members | PascalCase | `ProcessAttack`, `SymbolStore` |
| Private fields | `_camelCase` | `_connectionFactory`, `_logger` |
| Local variables, parameters | camelCase | `filePath`, `repoId` |
| Constants | PascalCase | `DefaultTimeout`, `MaxDepth` |
| Interfaces | `I` prefix + PascalCase | `ISymbolStore`, `ILanguageParser` |
| Test classes | `internal sealed class FooTests` | `CSharpParserTests` |
| Test methods | PascalCase (NO underscores) | `ParseMethodReturnsCorrectSymbol` |

## Code Style

- **Brace style:** Allman (opening brace on next line)
- **Indentation:** 4 spaces (2 for XML/JSON/YAML)
- **Namespaces:** File-scoped (`namespace Foo;`)
- **Using directives:** Outside namespace
- **var usage:** Use `var` when type is apparent; explicit type otherwise
- **Expression-bodied members:** Use for single-line methods/properties
- **Pattern matching:** Prefer `is` pattern over cast-check
- **Modifiers:** Always explicit (`public`, `private`, etc.); prefer `readonly` fields
- **Null checking:** Use `ArgumentNullException.ThrowIfNull()` for parameter validation

## Dependency Injection Patterns

- **Core registration:** `ServiceCollectionExtensions.AddCodeCompressCore()` registers all Core services
- **Server registration:** `ServiceCollectionExtensions.AddCodeCompressServer()` registers Server services
- **Parser registration:** Parsers are registered as `ILanguageParser` implementations — the `IndexEngine` auto-resolves by file extension. Adding a new parser = one class implementing `ILanguageParser`, registered via DI. No other wiring needed.
- **Singleton services:** `IFileHasher`, `IChangeTracker`, parsers
- **Scoped services:** `ISymbolStore` (per-project database connection)

## Key Interfaces

| Interface | Purpose | Implementation |
|-----------|---------|----------------|
| `ILanguageParser` | Parse source files into symbols | `CSharpParser`, `LuauParser`, `TerraformParser`, etc. |
| `ISymbolStore` | SQLite repository for symbols | `SqliteSymbolStore` |
| `IIndexEngine` | Orchestrates indexing pipeline | `IndexEngine` |
| `IFileHasher` | Parallel SHA-256 file hashing | `FileHasher` |
| `IChangeTracker` | Diff current vs stored hashes | `ChangeTracker` |
| `IPathValidator` | Path traversal prevention | `PathValidatorService` |
| `IConnectionFactory` | SQLite connection creation | `SqliteConnectionFactory` |
| `IProjectScopeFactory` | Scoped project access (DB + engine) | `ProjectScopeFactory` |

## Async/Await Conventions

- Async throughout — no blocking calls (no `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`)
- All async methods end with `Async` suffix
- Always use `.ConfigureAwait(false)` on awaited tasks
- Use `CancellationToken` on all async public API methods
- Use `Parallel.ForEachAsync` for parallel I/O (file hashing, parsing)

## Performance Conventions

- `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` for file content parsing (zero-copy)
- `ArrayPool<byte>` buffering in `FileHasher`
- SQLite: WAL mode, batch inserts via transactions, `PRAGMA synchronous=NORMAL`
- FTS5 virtual tables for full-text search (`symbols_fts`, `file_content_fts`)

## Analyzer Rules to Watch

These are common build errors (TreatWarningsAsErrors) that trip up new code:

| Rule | Issue | Fix |
|------|-------|-----|
| CA1515 | Types in Exe projects must be `internal` | Use `internal` not `public` in test/CLI projects |
| CA1852 | Internal classes with no subtypes must be `sealed` | Add `sealed` |
| CA1707 | No underscores in member names | PascalCase test methods, not Snake_Case |
| CA1819 | Properties should not return arrays | Use `IReadOnlyList<T>` |
| S3261 | Empty namespaces are errors | Ensure files have at least one type |
| S2094 | Empty classes are errors | Ensure classes have at least one member |

## Security Requirements (OWASP)

- **Path traversal:** All paths validated via `PathValidator` — `Path.GetFullPath()` + starts-with check
- **SQL injection:** Parameterized queries only (`@param` syntax). Zero string concatenation in SQL.
- **FTS5 injection:** Queries sanitized via `Fts5QuerySanitizer` before SQLite
- **Read-only:** No file modification tools. Source files are read-only.
- **No `dynamic` or `object`:** All types strongly typed
- **Output sanitization:** MCP responses are structured data only — never echo raw user input

## Key NuGet Packages

| Package | Purpose |
|---------|---------|
| ModelContextProtocol | MCP SDK — server hosting, tool registration |
| Microsoft.Data.Sqlite | SQLite access with FTS5 |
| Microsoft.Extensions.FileSystemGlobbing | Glob pattern matching |
| Microsoft.Extensions.Hosting | Generic host for DI, logging |
| TUnit | Testing framework (source-generated, AOT-compatible) |
| NSubstitute | Mocking framework |
| Verify | Snapshot testing |
| SonarAnalyzer.CSharp | Static analysis |
