# 003 — C# Sample Project and Integration Tests

## Summary

Create a realistic C# sample project and integration tests that verify the C# parser works correctly through the full pipeline. Also includes a mixed-language test confirming that Luau and C# parsers coexist and both contribute symbols when indexing a project containing both file types.

## Dependencies

- **Feature 12-001** — `CSharpParser` core patterns (namespace, class, interface, record, enum, struct).
- **Feature 12-002** — `CSharpParser` methods, properties, using statements.
- **Feature 11** — Integration test infrastructure and conventions (EndToEndTests pattern).

## Scope

### 1. C# Sample Project (`samples/csharp-sample-project/`)

A small .NET project with realistic patterns covering all C# parser extraction targets:

| File | Contents | Patterns Covered |
|---|---|---|
| `Models/Player.cs` | Record with properties, constructor | Record, primary constructor, namespace, properties |
| `Models/Inventory.cs` | Class with properties, nested enum | Class, properties, nested enum, XML doc comments |
| `Models/GameEvent.cs` | Abstract class, generic class, interfaces | Abstract class, generics, constraints, interface |
| `Services/ICombatService.cs` | Interface with method signatures | Interface, method declarations, XML doc comments |
| `Services/CombatService.cs` | Class implementing interface, DI, async methods | Class, constructor injection, async Task<T>, using statements |
| `Services/InventoryService.cs` | Service with CRUD operations, extension methods | Methods, expression-bodied members, extension method |
| `Handlers/GameHandler.cs` | Public API handler with attributes | Methods with attributes, nullable returns, tuples |
| `Enums/GameState.cs` | Enum with underlying type | Enum, flags |

Target: ~5-8 files, ~50-100 symbols, covering namespaces, classes, interfaces, records, enums, structs, methods, properties, constructors, generics, async, using statements, XML doc comments, attributes, and nested types.

### 2. Integration Tests (`tests/CodeCompress.Integration.Tests/CSharpEndToEndTests.cs`)

All tests use real components — real SQLite, real `CSharpParser`, real `IndexEngine`. No mocks.

| Test | Description |
|---|---|
| IndexProject_CSharpSample_CorrectFilesAndSymbolCounts | Index the C# sample project, verify expected file and symbol counts |
| ProjectOutline_CSharpSymbols_GroupedCorrectly | Call `project_outline`, verify C# symbols grouped by file with correct kinds |
| GetSymbol_CSharpMethod_ReturnsSourceCode | Call `get_symbol` for a known C# method, verify source code matches |
| GetSymbol_CSharpRecord_ReturnsDeclaration | Retrieve a record declaration, verify signature includes primary constructor |
| SearchSymbols_CSharpNames_ReturnsRankedResults | Search for C# symbol names, verify results ranked by relevance |
| GetModuleApi_CSharpService_ReturnsPublicApi | Call `get_module_api` for a C# service file, verify public API surface |
| DependencyGraph_UsingStatements_Captured | Call `dependency_graph`, verify using statement relationships appear |
| NamespaceExtraction_AllFilesHaveNamespace | Verify all C# files have their namespace extracted as a Module symbol |
| NestedTypes_ParentChainCorrect | Verify nested class/enum has correct ParentSymbol |
| XmlDocComments_CapturedOnSymbols | Verify XML doc comments are present on documented symbols |

### 3. Mixed-Language Integration Test (`tests/CodeCompress.Integration.Tests/MixedLanguageTests.cs`)

Tests that both parsers coexist when indexing a project with `.luau` and `.cs` files:

| Test | Description |
|---|---|
| MixedProject_BothParsersContribute | Create temp directory with both `.luau` and `.cs` files, index it, verify symbols from both languages |
| MixedProject_ProjectOutline_ShowsBothLanguages | `project_outline` includes Luau functions and C# classes |
| MixedProject_SearchSymbols_FindsBothLanguages | Search returns results from both `.luau` and `.cs` files |
| ParserSelection_ByExtension_Correct | `.cs` files use CSharpParser, `.luau` files use LuauParser — no cross-contamination |

### 4. Test Infrastructure

| Component | Detail |
|---|---|
| Database | Temp SQLite file per test, cleaned up in `[After(Test)]` |
| DI | Real service collection with both `LuauParser` and `CSharpParser` registered |
| Sample files | C# sample project committed to repo; mixed-language tests create temp files |
| Assertions | TUnit async/fluent style |

## Acceptance Criteria

- [ ] C# sample project exists at `samples/csharp-sample-project/` with 5-8 `.cs` files.
- [ ] Sample project covers: namespaces, classes, interfaces, records, enums, methods, properties, generics, using statements, XML doc comments, nested types, async patterns.
- [ ] Sample project contains ~50-100 symbols.
- [ ] All C# integration tests use real components — no mocks.
- [ ] Index test verifies correct file and symbol counts for the C# sample.
- [ ] `project_outline` shows C# symbols with correct kinds and signatures.
- [ ] `get_symbol` retrieves C# source code accurately.
- [ ] `search_symbols` finds C# symbols by name.
- [ ] `dependency_graph` reflects using statement relationships.
- [ ] Mixed-language tests verify both parsers contribute symbols in a shared project.
- [ ] Parser selection by file extension is correct (no `.cs` files parsed by Luau parser or vice versa).
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Integration.Tests`.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `samples/csharp-sample-project/Models/Player.cs` | Record with properties |
| `samples/csharp-sample-project/Models/Inventory.cs` | Class with nested enum |
| `samples/csharp-sample-project/Models/GameEvent.cs` | Abstract class, generics, interfaces |
| `samples/csharp-sample-project/Services/ICombatService.cs` | Interface with method signatures |
| `samples/csharp-sample-project/Services/CombatService.cs` | Service implementation with async methods |
| `samples/csharp-sample-project/Services/InventoryService.cs` | Service with extension methods |
| `samples/csharp-sample-project/Handlers/GameHandler.cs` | Handler with attributes and complex signatures |
| `samples/csharp-sample-project/Enums/GameState.cs` | Enum with underlying type |
| `tests/CodeCompress.Integration.Tests/CSharpEndToEndTests.cs` | C# parser integration tests |
| `tests/CodeCompress.Integration.Tests/MixedLanguageTests.cs` | Mixed-language integration tests |

### Modify

| File | Description |
|---|---|
| `tests/CodeCompress.Integration.Tests/CodeCompress.Integration.Tests.csproj` | Ensure project references to Core and Server are present |

## Out of Scope

- C# project file parsing (`.csproj`) — only source files are indexed.
- NuGet dependency resolution — using statements are the dependency signal, not package references.
- Generated files (`*.g.cs`, `*.designer.cs`) — not explicitly excluded but not specifically tested.
- Solution-level indexing (multiple C# projects) — each project is indexed independently.

## Notes / Decisions

1. **Sample project without `.csproj`.** The sample C# files are standalone — they do not need to compile as a real .NET project. They are test fixtures for the parser, not build targets. This avoids needing a second SDK or build step.
2. **Mixed-language tests.** These are critical for verifying the `IndexEngine`'s parser dispatch logic — it must correctly select the parser based on file extension. A `.cs` file must go to `CSharpParser` and a `.luau` file to `LuauParser`, with no interference.
3. **Using statements as dependency graph edges.** The C# dependency graph will be namespace-based rather than file-based. This is less precise than Luau's `require()` but still useful for understanding project structure. A file that has `using MyApp.Services;` depends on the namespace `MyApp.Services`.
4. **Symbol count stability.** As with the Luau integration tests, exact symbol counts in assertions serve as regression guards. If the parser changes behavior, these tests catch it immediately.
