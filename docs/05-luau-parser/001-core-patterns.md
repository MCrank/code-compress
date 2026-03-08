# 001 — Luau Parser: Core Function/Method/Class Pattern Extraction

## Summary

Implement the first language parser for CodeCompress (MVP). `LuauParser` is a regex/pattern-based parser for Luau (Roblox Lua variant) that extracts core symbol types: module classes, methods, functions, local functions, and module return statements. Luau syntax is regular enough that full AST parsing is unnecessary — line-oriented regex matching over `ReadOnlySpan<byte>` provides accurate extraction with zero-copy performance.

## Dependencies

- **Feature 01** — Project scaffold, solution file, and `.csproj` files for `CodeCompress.Core` and `CodeCompress.Core.Tests`.
- **Feature 02** — Core models and interfaces (`ILanguageParser`, `SymbolInfo`, `SymbolKind`, `Visibility`, `ParseResult`, `DependencyInfo`).

## Scope

### 1. LuauParser Class (`src/CodeCompress.Core/Parsers/LuauParser.cs`)

Implements `ILanguageParser` with:

| Member           | Value / Signature                                                  |
|------------------|--------------------------------------------------------------------|
| `LanguageId`     | `"luau"`                                                           |
| `FileExtensions` | `[".luau", ".lua"]`                                                |
| `Parse`          | `ParseResult Parse(string filePath, ReadOnlySpan<byte> content)`   |

### 2. Core Symbol Patterns

The parser must recognize and extract the following Luau patterns:

#### Module Class Declaration

```lua
local ClassName = {} :: ClassName
```

- **Kind:** `SymbolKind.Class`
- **Visibility:** `Visibility.Public`
- **Name:** `ClassName`
- **Signature:** Full declaration line

#### Method Declaration

```lua
function ClassName:MethodName(params): ReturnType
```

- **Kind:** `SymbolKind.Method`
- **Visibility:** `Visibility.Public` (if on a returned/exported class), `Visibility.Private` otherwise
- **Name:** `MethodName`
- **ParentSymbol:** `ClassName`
- **Signature:** Full declaration line including parameters and return type

#### Top-Level Function Declaration

```lua
function functionName(params): ReturnType
```

- **Kind:** `SymbolKind.Function`
- **Visibility:** `Visibility.Public`
- **Name:** `functionName`
- **Signature:** Full declaration line

#### Local Function Declaration

```lua
local function name(params): ReturnType
```

- **Kind:** `SymbolKind.Function`
- **Visibility:** `Visibility.Private`
- **Name:** `name`
- **Signature:** Full declaration line

#### Module Return Statement

```lua
return ClassName
```

- **Kind:** `SymbolKind.Export`
- **Visibility:** `Visibility.Public`
- **Name:** `ClassName`
- **Signature:** `return ClassName`

### 3. Symbol Metadata Capture

For every extracted symbol, the parser must populate:

| Field          | Derivation                                                                 |
|----------------|----------------------------------------------------------------------------|
| `Name`         | Extracted from regex capture group                                         |
| `Kind`         | Determined by pattern match (see above)                                    |
| `Signature`    | Full declaration line, trimmed                                             |
| `ParentSymbol` | Class name for methods; `null` for top-level symbols                       |
| `ByteOffset`   | Byte position of the first character of the declaration in the source file |
| `ByteLength`   | Byte length from declaration start to matching `end` keyword               |
| `LineStart`    | 1-based line number of the declaration                                     |
| `LineEnd`      | 1-based line number of the corresponding `end` keyword                     |
| `Visibility`   | Determined by pattern and export analysis (see above)                      |
| `DocComment`   | `null` (deferred to 05-002)                                               |

### 4. End-Keyword Matching

Functions, methods, and local functions in Luau are terminated by an `end` keyword. The parser must track nesting depth (counting `function`, `if`, `for`, `while`, `do`, `repeat` openers against `end` / `until` closers) to find the correct closing `end` for each symbol, yielding accurate `ByteLength` and `LineEnd` values.

### 5. ReadOnlySpan Processing

- Decode `ReadOnlySpan<byte>` as UTF-8.
- Process line-by-line for regex matching.
- Track cumulative byte offsets for each line to compute `ByteOffset` per symbol.

### 6. DI Registration

`LuauParser` must be registered as an `ILanguageParser` implementation in the DI container. The `IndexEngine` auto-resolves parsers by file extension, so no additional wiring is needed beyond registration. Adding a new language parser requires only one class — no other changes.

## Acceptance Criteria

- [ ] `LuauParser` implements `ILanguageParser` with `LanguageId = "luau"` and `FileExtensions = [".luau", ".lua"]`.
- [ ] `Parse` accepts `ReadOnlySpan<byte>` content and returns a `ParseResult`.
- [ ] Module class declarations (`local X = {} :: X`) are extracted as `SymbolKind.Class` with `Visibility.Public`.
- [ ] Method declarations (`function X:Y(...)`) are extracted as `SymbolKind.Method` with correct `ParentSymbol`.
- [ ] Top-level function declarations are extracted as `SymbolKind.Function` with `Visibility.Public`.
- [ ] Local function declarations are extracted as `SymbolKind.Function` with `Visibility.Private`.
- [ ] Module return statements are extracted as `SymbolKind.Export`.
- [ ] `ByteOffset`, `ByteLength`, `LineStart`, and `LineEnd` are accurate for all symbols.
- [ ] End-keyword nesting is correctly tracked (nested blocks do not confuse symbol boundaries).
- [ ] Empty file input returns an empty `ParseResult` (zero symbols, zero dependencies).
- [ ] File with only comments returns an empty `ParseResult`.
- [ ] File with multiple symbols returns all symbols in declaration order.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Core.Tests`.
- [ ] Parser test coverage is 95%+.

## Files to Create/Modify

### Create

| File | Description |
|------|-------------|
| `src/CodeCompress.Core/Parsers/LuauParser.cs` | Luau language parser implementation |
| `tests/CodeCompress.Core.Tests/Parsers/LuauParserTests.cs` | Parser unit tests |

### Modify

| File | Description |
|------|-------------|
| DI registration site (e.g., `ServiceCollectionExtensions.cs`) | Register `LuauParser` as `ILanguageParser` |

## Test Cases (`tests/CodeCompress.Core.Tests/Parsers/LuauParserTests.cs`)

| Test | Description |
|------|-------------|
| Parse_SingleFunction_ReturnsCorrectSymbolInfo | Top-level `function foo()` yields one `Function` symbol with correct metadata |
| Parse_MethodOnClass_ReturnsCorrectParentAndVisibility | `function Cls:bar()` yields `Method` with `ParentSymbol = "Cls"` |
| Parse_LocalFunction_ReturnsPrivateVisibility | `local function helper()` yields `Function` with `Visibility.Private` |
| Parse_ModuleClassDeclaration_ReturnsClassSymbol | `local Cls = {} :: Cls` yields `Class` with `Visibility.Public` |
| Parse_ModuleReturn_ReturnsExportSymbol | `return Cls` yields `Export` symbol |
| Parse_MultipleSymbols_ReturnsAllInOrder | File with class, method, function, local function, return yields all five |
| Parse_EmptyFile_ReturnsEmptyResult | Zero-byte input returns empty `ParseResult` |
| Parse_OnlyComments_ReturnsEmptyResult | File containing only `--` comments returns empty `ParseResult` |
| Parse_ByteOffsets_AreAccurate | Byte offsets match actual positions in the source bytes |
| Parse_LineNumbers_AreOneBased | `LineStart` and `LineEnd` are 1-based and correct |
| Parse_NestedBlocks_DoNotConfuseEndMatching | Function containing `if`/`for`/`while` blocks resolves correct `end` |
| Parse_FunctionWithReturnType_CapturesFullSignature | `function foo(): string` signature includes return type annotation |
| Parse_MethodWithParameters_CapturesFullSignature | `function Cls:bar(x: number, y: string): boolean` captures full signature |

## Out of Scope

- Type definitions (`export type`, `type`) — covered in 05-002.
- Constants (SCREAMING_SNAKE_CASE detection) — covered in 05-002.
- Dependency extraction (`require()`) — covered in 05-002.
- Doc comment capture — covered in 05-002.
- Multi-line type definitions and brace-depth tracking — covered in 05-002.
- Metatable pattern detection — covered in 05-002.
- Generic/union/intersection types — covered in 05-002.
- Integration with IndexEngine or SQLite storage — separate features.

## Notes / Decisions

1. **Regex over AST.** Luau syntax is sufficiently regular (no preprocessor, no significant indentation semantics, clear keyword delimiters) that regex-based line matching provides reliable extraction without the complexity and dependency cost of a full parser. This aligns with the project's stated approach for Phase 1.
2. **End-keyword nesting.** Luau uses `end` to close multiple block types (`function`, `if`, `for`, `while`, `do`) and `until` to close `repeat` blocks. The parser must maintain a nesting counter to pair each symbol's opening line with its correct `end`. This is the most complex part of the parser logic.
3. **Visibility heuristic.** Top-level `function` declarations and class declarations are treated as `Public`. `local function` declarations are `Private`. Method visibility depends on whether its parent class is the module's return value — this requires a two-pass approach or post-processing after the return statement is found.
4. **UTF-8 assumption.** Luau source files are assumed to be UTF-8 encoded. The parser decodes `ReadOnlySpan<byte>` to string lines via `Encoding.UTF8`. Byte offsets are computed on the raw bytes, not on decoded characters, ensuring accuracy for multi-byte content.
5. **Dependencies list.** The `ParseResult.Dependencies` list will be empty in this plan; dependency extraction is deferred to 05-002.
