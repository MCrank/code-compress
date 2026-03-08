# 002 — Luau Parser: Types, Constants, Dependencies, and Edge Cases

## Summary

Extend the Luau parser with extraction of type definitions, constants, `require()` dependencies, and doc comments. Handle edge cases including multi-line type definitions, generic types, union/intersection types, metatable OOP patterns, nested functions, and malformed input. This plan brings `LuauParser` to full feature completeness for the MVP.

## Dependencies

- **Feature 05-001** — Core `LuauParser` implementation with function, method, class, and export extraction already working and tested.

## Scope

### 1. Additional Symbol Patterns

#### Export Type Definition

```lua
export type TypeName = { field: string, count: number }
```

- **Kind:** `SymbolKind.Type`
- **Visibility:** `Visibility.Public`
- **Name:** `TypeName`
- **Signature:** Full declaration (first line for multi-line types, or entire declaration for single-line)
- **ByteLength:** Spans from `export` keyword to closing `}` (for table types) or end of declaration

#### Local Type Definition

```lua
type TypeName = { field: string }
```

- **Kind:** `SymbolKind.Type`
- **Visibility:** `Visibility.Private`
- **Name:** `TypeName`
- **Signature:** Full declaration

#### Generic Type Definition

```lua
export type Result<T> = { ok: boolean, value: T? }
```

- Parsed identically to non-generic types; the generic parameter list `<T>` is included in the `Name` (as `Result<T>`) and in the `Signature`.

#### Union and Intersection Types

```lua
type Status = "active" | "downed" | "dead"
export type Combined = TypeA & TypeB
```

- Extracted as `SymbolKind.Type` with appropriate visibility.
- `Signature` captures the full right-hand side.

#### Constant Declaration

```lua
local MAX_RETRIES = 5
local DEFAULT_TIMEOUT = 30
```

- **Detection:** `local IDENTIFIER = value` where `IDENTIFIER` matches `^[A-Z][A-Z0-9_]+$` (SCREAMING_SNAKE_CASE).
- **Kind:** `SymbolKind.Constant`
- **Visibility:** `Visibility.Private`
- **Name:** The constant identifier
- **Signature:** Full declaration line

### 2. Dependency Extraction

#### Path-Style Require

```lua
local Module = require(path.to.Module)
local Service = require(script.Parent.ServiceName)
```

- **RequirePath:** `path.to.Module` or `script.Parent.ServiceName` (the full expression inside `require(...)`)
- **Alias:** `Module` or `Service` (the local variable name)

#### String-Style Require

```lua
local Http = require("HttpService")
```

- **RequirePath:** `HttpService` (the string literal contents, without quotes)
- **Alias:** `Http`

#### Bare Require (No Alias)

```lua
require(script.Parent.Init)
```

- **RequirePath:** `script.Parent.Init`
- **Alias:** `null`

### 3. Doc Comment Capture

Luau uses `--` for single-line comments. A doc comment is defined as one or more contiguous `--` comment lines immediately preceding a symbol declaration (no blank lines between the comment block and the declaration).

```lua
-- Calculates the distance between two points.
-- Returns the Euclidean distance as a number.
function calculateDistance(a: Vector3, b: Vector3): number
```

- The concatenated comment text (without leading `-- ` prefixes) is stored in `SymbolInfo.DocComment`.
- Non-contiguous comments (separated by blank lines from the declaration) are not captured.

### 4. Multi-Line Type Definitions

Type definitions using table syntax can span many lines:

```lua
export type PlayerData = {
    name: string,
    health: number,
    inventory: { Item },
    position: Vector3,
}
```

The parser must track brace depth (`{` increments, `}` decrements) starting from the opening `{` to find the matching closing `}`, yielding correct `ByteLength` and `LineEnd` values.

### 5. Metatable Pattern Detection

Luau OOP classes commonly use a metatable setup pattern:

```lua
local ClassName = {}
ClassName.__index = ClassName

function ClassName.new(...)
    local self = (setmetatable :: any)({}, ClassName)
    -- ...
    return self
end
```

- The `local ClassName = {}` followed by `ClassName.__index = ClassName` pattern should be recognized as a `SymbolKind.Class` declaration (if not already matched by the `local X = {} :: X` pattern from 05-001).
- `ClassName.new(...)` is a constructor — extracted as `SymbolKind.Method` with `ParentSymbol = "ClassName"`.
- The `(setmetatable :: any)(self, ClassName)` line inside a function body is not itself a symbol, but its presence reinforces the class identification.

### 6. Edge Cases

| Scenario | Expected Behavior |
|----------|-------------------|
| Multi-line function signatures | Signature captures the full opening line; `ByteOffset` starts at `function` keyword |
| Nested functions inside functions | Inner function is extracted with correct `ParentSymbol` set to the enclosing function name |
| Multiple return values in type annotations | `function foo(): (string, number)` — full return type captured in signature |
| Empty type body `type X = {}` | Single-line type, `ByteLength` covers the full line |
| Malformed/incomplete declaration (e.g., `function` with no name) | Gracefully skipped — no crash, no symbol emitted, parsing continues |
| Unterminated type (EOF before closing `}`) | Emit symbol with `LineEnd` set to last line of file; `ByteLength` covers to EOF |
| Unterminated function (EOF before `end`) | Emit symbol with `LineEnd` set to last line of file |
| Mixed symbol types in one file | All symbol types extracted in declaration order |
| Very long files (10,000+ lines) | Parser completes without performance degradation |
| Binary/non-UTF-8 content | Graceful handling — return empty `ParseResult` or best-effort extraction |

### 7. Dot-Syntax Static Methods

```lua
function ClassName.staticMethod(params): ReturnType
```

- **Kind:** `SymbolKind.Method`
- **Visibility:** Determined by parent class export status
- **ParentSymbol:** `ClassName`
- Distinguished from colon-syntax instance methods but extracted with the same `SymbolKind`

## Acceptance Criteria

- [ ] `export type` declarations are extracted as `SymbolKind.Type` with `Visibility.Public`.
- [ ] `type` (non-export) declarations are extracted as `SymbolKind.Type` with `Visibility.Private`.
- [ ] Generic type parameters are captured in the name and signature (e.g., `Result<T>`).
- [ ] Union types (`"a" | "b"`) and intersection types (`A & B`) are extracted correctly.
- [ ] SCREAMING_SNAKE_CASE locals are extracted as `SymbolKind.Constant` with `Visibility.Private`.
- [ ] `require(path.to.Module)` produces a `DependencyInfo` with correct `RequirePath` and `Alias`.
- [ ] `require("StringModule")` produces a `DependencyInfo` with the string contents as `RequirePath`.
- [ ] Bare `require()` calls (no alias) produce a `DependencyInfo` with `Alias = null`.
- [ ] Contiguous `--` comment blocks preceding a symbol are captured in `DocComment`.
- [ ] Multi-line type definitions have correct `ByteLength` and `LineEnd` via brace-depth tracking.
- [ ] Metatable pattern (`X = {}` + `X.__index = X`) is recognized as a class declaration.
- [ ] Dot-syntax static methods (`function X.y()`) are extracted as methods with correct `ParentSymbol`.
- [ ] Nested functions have correct `ParentSymbol` referencing the enclosing function.
- [ ] Malformed declarations are gracefully skipped without crashing the parser.
- [ ] Unterminated symbols (EOF before `end` or `}`) are handled with best-effort `LineEnd`/`ByteLength`.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Core.Tests`.
- [ ] Combined parser test coverage (05-001 + 05-002) is 95%+.

## Files to Create/Modify

### Create

None. All new tests are added to the existing test file.

### Modify

| File | Description |
|------|-------------|
| `src/CodeCompress.Core/Parsers/LuauParser.cs` | Add type, constant, dependency, doc comment, and metatable extraction logic |
| `tests/CodeCompress.Core.Tests/Parsers/LuauParserTests.cs` | Add test cases for all new patterns and edge cases |

## Test Cases (additions to `LuauParserTests.cs`)

| Test | Description |
|------|-------------|
| Parse_ExportType_ReturnsPublicTypeSymbol | `export type Foo = { ... }` yields `Type` with `Visibility.Public` |
| Parse_LocalType_ReturnsPrivateTypeSymbol | `type Bar = { ... }` yields `Type` with `Visibility.Private` |
| Parse_GenericType_CapturesTypeParameters | `export type Result<T>` name includes `<T>` |
| Parse_UnionType_ExtractsCorrectly | `type Status = "a" \| "b"` yields `Type` with full signature |
| Parse_IntersectionType_ExtractsCorrectly | `type Combined = A & B` yields `Type` with full signature |
| Parse_MultiLineType_CorrectByteLength | Brace-depth tracking yields accurate span for multi-line type body |
| Parse_Constant_ScreamingSnakeCase_ReturnsConstantSymbol | `local MAX_RETRIES = 5` yields `Constant` with `Visibility.Private` |
| Parse_NonConstantLocal_IsNotExtractedAsConstant | `local myVar = 5` is not treated as a constant |
| Parse_RequirePathStyle_ReturnsDependency | `local M = require(path.to.M)` yields `DependencyInfo` with correct path and alias |
| Parse_RequireStringStyle_ReturnsDependency | `local H = require("Http")` yields `DependencyInfo` with `RequirePath = "Http"` |
| Parse_RequireNoAlias_ReturnsDependencyWithNullAlias | Bare `require(...)` yields `Alias = null` |
| Parse_DocComment_CapturedOnSymbol | Contiguous `--` block before function populates `DocComment` |
| Parse_NonContiguousComment_NotCaptured | Comment separated by blank line from declaration yields `DocComment = null` |
| Parse_MetatablePattern_RecognizedAsClass | `X = {}` + `X.__index = X` yields `Class` symbol |
| Parse_DotSyntaxMethod_ExtractsWithParent | `function Cls.static()` yields `Method` with `ParentSymbol = "Cls"` |
| Parse_NestedFunction_CorrectParent | Inner function has `ParentSymbol` set to enclosing function |
| Parse_MalformedDeclaration_GracefullySkipped | `function` with no name does not crash; other symbols still extracted |
| Parse_UnterminatedFunction_HandledGracefully | EOF before `end` yields symbol with `LineEnd` at last line |
| Parse_UnterminatedType_HandledGracefully | EOF before closing `}` yields symbol with best-effort span |
| Parse_MixedFile_AllSymbolTypes | File with class, method, function, type, constant, require, export yields all correctly |
| Parse_MultipleReturnTypes_CapturedInSignature | `function foo(): (string, number)` signature includes full return annotation |

## Out of Scope

- Integration with `IndexEngine` or SQLite storage — separate features.
- Incremental parsing (only re-parsing changed regions of a file) — potential future optimization.
- Semantic analysis (resolving types across files, type checking) — out of scope for a pattern-based parser.
- Other languages (C#, Python, TypeScript) — separate features per the phased rollout.
- Performance benchmarking — may be added as a separate plan if needed.

## Notes / Decisions

1. **Brace-depth tracking for types.** Multi-line table types (`{ ... }`) require tracking `{`/`}` nesting depth, similar to how 05-001 tracks `end`-keyword nesting for functions. Both mechanisms coexist in the parser — function-like symbols use end-keyword matching, type symbols use brace-depth matching.
2. **SCREAMING_SNAKE_CASE heuristic.** Constants are detected purely by naming convention (`^[A-Z][A-Z0-9_]+$`). This is a pragmatic heuristic — Luau has no `const` keyword. False positives (e.g., `local HTTP_STATUS = require(...)`) are acceptable since the symbol is still meaningful to index. The regex requires at least two characters and at least one underscore or digit to avoid matching single-letter capitals.
3. **Doc comment convention.** Luau does not have a standardized doc comment format (no `///` or `/** */` equivalent). We treat any contiguous block of `--` comments immediately preceding a declaration as a doc comment. `---` (triple-dash) comments are treated identically to `--` comments. The leading `-- ` prefix (including the space) is stripped from each line.
4. **Metatable as fallback class detection.** The `X.__index = X` pattern is a secondary class detection heuristic, complementing the `local X = {} :: X` type annotation pattern from 05-001. If both patterns match the same name, only one `Class` symbol is emitted (no duplicates).
5. **Two-pass visibility resolution.** Determining whether a method's parent class is exported requires knowing the module's `return` statement. The parser uses a two-pass approach: first pass extracts all symbols with provisional visibility, second pass adjusts method visibility based on whether their parent class name matches the returned value.
6. **Dependency extraction is position-independent.** `require()` calls are extracted wherever they appear in the file (top-level, inside functions, inside conditional blocks). All are recorded as dependencies since they represent module-level relationships regardless of runtime conditionality.
