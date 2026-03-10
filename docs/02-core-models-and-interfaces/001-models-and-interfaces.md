# 001 — Core Models and Interfaces

## Summary

Define all core models, enums, and interfaces that parsers, storage, and the indexing engine depend on. These types form the foundational contract for the entire CodeCompress pipeline: language parsers produce `ParseResult` values containing `SymbolInfo` and `DependencyInfo` records, and downstream components (storage, index engine, MCP tools) consume them.

## Dependencies

- **Feature 01** — Project scaffold and `.csproj` files must exist (`CodeCompress.Core`, `CodeCompress.Core.Tests`) with `Directory.Build.props`, `Directory.Packages.props`, and `global.json` in place.

## Scope

### Models (`src/CodeCompress.Core/Models/`)

#### `SymbolKind` enum

Represents the category of a parsed symbol.

| Value      | Description                        |
|------------|------------------------------------|
| Function   | Top-level or free function         |
| Method     | Function attached to a type/class  |
| Type       | Type alias or typedef              |
| Class      | Class declaration                  |
| Interface  | Interface declaration              |
| Export     | Exported value (e.g., Luau module) |
| Constant   | Constant or read-only binding      |
| Module     | Module or namespace                |

#### `Visibility` enum

Describes the access level of a symbol.

| Value   | Description                      |
|---------|----------------------------------|
| Public  | Accessible outside its module    |
| Private | Accessible only within its type  |
| Local   | Scoped to the enclosing block    |

#### `SymbolInfo` record

Immutable record representing a single parsed symbol.

| Property      | Type            | Nullable | Description                                  |
|---------------|-----------------|----------|----------------------------------------------|
| Name          | `string`        | No       | Symbol name                                  |
| Kind          | `SymbolKind`    | No       | Category of the symbol                       |
| Signature     | `string`        | No       | Full declaration signature                   |
| ParentSymbol  | `string?`       | Yes      | Enclosing symbol name, if nested             |
| ByteOffset    | `int`           | No       | Start byte offset in the source file         |
| ByteLength    | `int`           | No       | Length in bytes of the symbol's source span  |
| LineStart     | `int`           | No       | 1-based start line                           |
| LineEnd       | `int`           | No       | 1-based end line                             |
| Visibility    | `Visibility`    | No       | Access level                                 |
| DocComment    | `string?`       | Yes      | Leading doc comment, if present              |

#### `DependencyInfo` record

Immutable record representing a dependency (require/import).

| Property    | Type      | Nullable | Description                          |
|-------------|-----------|----------|--------------------------------------|
| RequirePath | `string`  | No       | The path or module being required    |
| Alias       | `string?` | Yes      | Local alias assigned at import site  |

#### `ParseResult` record

Immutable record returned by every language parser.

| Property     | Type                              | Description                |
|--------------|-----------------------------------|----------------------------|
| Symbols      | `IReadOnlyList<SymbolInfo>`       | All symbols found in file  |
| Dependencies | `IReadOnlyList<DependencyInfo>`   | All dependencies in file   |

### Interface (`src/CodeCompress.Core/Parsers/`)

#### `ILanguageParser`

Contract that every language parser must implement.

| Member           | Type                  | Description                                           |
|------------------|-----------------------|-------------------------------------------------------|
| `LanguageId`     | `string` (get)        | Unique identifier, e.g. `"luau"`, `"csharp"`         |
| `FileExtensions` | `string[]` (get)      | Extensions this parser handles, e.g. `[".luau"]`     |
| `Parse`          | method                | `ParseResult Parse(string filePath, ReadOnlySpan<byte> content)` |

### Tests (`tests/CodeCompress.Core.Tests/Models/`)

- **SymbolKindTests** — Verify all expected enum members exist and have correct underlying values.
- **VisibilityTests** — Verify all expected enum members exist and have correct underlying values.
- **SymbolInfoTests** — Construct instances, verify property access, confirm record value equality and inequality, test nullable properties (`ParentSymbol`, `DocComment`) with `null`.
- **DependencyInfoTests** — Construct instances, verify equality, test `Alias` as `null`.
- **ParseResultTests** — Construct with empty and non-empty lists, verify `Symbols` and `Dependencies` contents, confirm record equality.

## Acceptance Criteria

- [ ] `SymbolKind` enum is defined with all eight members in `src/CodeCompress.Core/Models/SymbolKind.cs`.
- [ ] `Visibility` enum is defined with all three members in `src/CodeCompress.Core/Models/Visibility.cs`.
- [ ] `SymbolInfo` record is defined with all ten properties in `src/CodeCompress.Core/Models/SymbolInfo.cs`.
- [ ] `DependencyInfo` record is defined with two properties in `src/CodeCompress.Core/Models/DependencyInfo.cs`.
- [ ] `ParseResult` record is defined with two properties in `src/CodeCompress.Core/Models/ParseResult.cs`.
- [ ] `ILanguageParser` interface is defined in `src/CodeCompress.Core/Parsers/ILanguageParser.cs`.
- [ ] All types use `readonly record struct` or `record` as appropriate — no `dynamic` or `object` types.
- [ ] All nullable properties use explicit `?` annotations.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All model and enum tests pass via `dotnet test tests/CodeCompress.Core.Tests`.
- [ ] Test coverage for models is 95%+.

## Files to Create/Modify

### Create

| File | Description |
|------|-------------|
| `src/CodeCompress.Core/Models/SymbolKind.cs` | `SymbolKind` enum |
| `src/CodeCompress.Core/Models/Visibility.cs` | `Visibility` enum |
| `src/CodeCompress.Core/Models/SymbolInfo.cs` | `SymbolInfo` record |
| `src/CodeCompress.Core/Models/DependencyInfo.cs` | `DependencyInfo` record |
| `src/CodeCompress.Core/Models/ParseResult.cs` | `ParseResult` record |
| `src/CodeCompress.Core/Parsers/ILanguageParser.cs` | `ILanguageParser` interface |
| `tests/CodeCompress.Core.Tests/Models/SymbolKindTests.cs` | Enum member tests |
| `tests/CodeCompress.Core.Tests/Models/VisibilityTests.cs` | Enum member tests |
| `tests/CodeCompress.Core.Tests/Models/SymbolInfoTests.cs` | Record construction, equality, null handling tests |
| `tests/CodeCompress.Core.Tests/Models/DependencyInfoTests.cs` | Record construction, equality tests |
| `tests/CodeCompress.Core.Tests/Models/ParseResultTests.cs` | Record construction, list contents tests |

### Modify

None. This feature introduces new files only.

## Out of Scope

- Parser implementations (covered by later features per language).
- Storage layer / SQLite schema (separate feature).
- Index engine orchestration (separate feature).
- MCP tool definitions (separate feature).
- Validation logic for model fields (e.g., `ByteOffset >= 0`) — may be added in a future hardening pass.

## Notes / Decisions

- **Records over classes.** All models are C# records to get value equality, immutability, and `with`-expression support for free. This aligns with the project's emphasis on strongly typed, immutable data flowing through the pipeline.
- **`ReadOnlySpan<byte>` on `Parse`.** The parser interface accepts `ReadOnlySpan<byte>` for the file content parameter to support zero-copy parsing as specified in the performance conventions. Callers are responsible for reading the file into a byte buffer.
- **`string[]` for `FileExtensions`.** Kept as a simple array rather than `IReadOnlyList<string>` for brevity; parsers are expected to return small, fixed arrays.
- **No base value for enums.** Both `SymbolKind` and `Visibility` use default `int` backing with auto-assigned values starting at 0. Explicit numbering is not required since these are not persisted as integers in the current design (SQLite stores the string name).
- **Namespace convention.** All models live under `CodeCompress.Core.Models`; the parser interface lives under `CodeCompress.Core.Parsers`.
