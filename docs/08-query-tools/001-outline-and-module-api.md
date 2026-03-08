# 001 — QueryTools: project_outline and get_module_api

## Summary

Implement the two highest-value query tools — `project_outline` delivers a compressed, token-efficient overview of an entire codebase (signatures only, no bodies), and `get_module_api` returns the full public API surface of a single module. Together these tools enable an AI agent to understand codebase structure at a fraction of the token cost of loading raw source files. Both tools return structured data with all output fields sanitized against prompt injection.

## Dependencies

- **Feature 07** — MCP server host setup and DI registration.
- **Feature 03** — `SymbolStore` queries (`GetProjectOutline`, `GetModuleApi`).
- **Feature 04** — `PathValidator` for input sanitization.

## Scope

### 1. QueryTools Class (`src/CodeCompress.Server/Tools/QueryTools.cs`)

Decorated with `[McpServerToolType]`. Receives `SymbolStore` and `PathValidator` via constructor injection. This class will hold all query tools across plans 08-001, 08-002, and 08-003.

### 2. project_outline Tool

```csharp
[McpServerTool(Name = "project_outline")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `include_private` | `bool` | No | `false` | Include private/local symbols |
| `group_by` | `string` | No | `"file"` | Grouping: `"file"`, `"kind"`, or `"directory"` |
| `max_depth` | `int?` | No | `null` (unlimited) | Limit directory traversal depth |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Validate `group_by` is one of `"file"`, `"kind"`, `"directory"` — reject invalid values.
3. Call `SymbolStore.GetProjectOutlineAsync(repoId, includePrivate, cancellationToken)`.
4. Format the result into structured markdown grouped by the selected strategy:

**group_by = "file" (default):**
```
# Project Outline: {name} ({file_count} files, {symbol_count} symbols)
## src/services/
### CombatService.luau (12 symbols)
  public class CombatService
  public method CombatService:ProcessAttack(attacker, target): DamageResult
  public method CombatService:CalculateDamage(stats): number
  private function validateTarget(target): boolean
## src/utils/
### MathUtils.luau (4 symbols)
  public function lerp(a: number, b: number, t: number): number
```

**group_by = "kind":**
```
# Project Outline: {name} ({file_count} files, {symbol_count} symbols)
## Classes (5)
  CombatService — src/services/CombatService.luau:3
  InventoryManager — src/services/InventoryManager.luau:1
## Functions (23)
  lerp — src/utils/MathUtils.luau:5
  clamp — src/utils/MathUtils.luau:12
## Methods (41)
  CombatService:ProcessAttack — src/services/CombatService.luau:8
```

**group_by = "directory":**
```
# Project Outline: {name}
## src/ (3 subdirectories)
### src/services/ (2 files, 24 symbols)
    CombatService.luau: class CombatService, 6 methods
    InventoryManager.luau: class InventoryManager, 4 methods
### src/utils/ (1 file, 4 symbols)
    MathUtils.luau: 4 functions
```

5. Apply `max_depth` filtering — if set, truncate the directory tree at the specified depth (1 = top-level only).
6. Apply `include_private` — if `false`, omit symbols with `Visibility.Private`.
7. **Token budget target:** ~3-8k tokens regardless of codebase size. Signatures only, no function bodies.

### 3. get_module_api Tool

```csharp
[McpServerTool(Name = "get_module_api")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `module_path` | `string` | Yes | — | Relative path to the module file (e.g., `"src/services/CombatService.luau"`) |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Validate `module_path` — must be a relative path, must not contain `..`, and when combined with `path` must resolve to a file within the project root.
3. Call `SymbolStore.GetModuleApiAsync(repoId, modulePath, cancellationToken)`.
4. Return structured result containing:

```json
{
  "module": "src/services/CombatService.luau",
  "exports": ["CombatService"],
  "symbols": [
    {
      "name": "CombatService",
      "kind": "class",
      "signature": "local CombatService = {} :: CombatService",
      "line": 3,
      "doc_comment": null
    },
    {
      "name": "ProcessAttack",
      "kind": "method",
      "parent": "CombatService",
      "signature": "function CombatService:ProcessAttack(attacker: Player, target: Player): DamageResult",
      "parameters": ["attacker: Player", "target: Player"],
      "return_type": "DamageResult",
      "line": 8,
      "doc_comment": "-- Processes an attack between two players"
    }
  ],
  "dependencies": [
    { "module": "src/utils/MathUtils", "symbols": ["clamp", "lerp"] }
  ]
}
```

5. Non-existent module returns structured error: `{ "error": "Module not found", "code": "MODULE_NOT_FOUND" }`.

### 4. Security Considerations

- All output fields are structured data — no freeform text echoing user input.
- Symbol names, signatures, and doc comments originate from source files (untrusted). These are returned as structured JSON fields, not embedded in prose or instructions.
- `module_path` is validated as a relative path and canonicalized against the project root to prevent traversal.
- `group_by` is validated against an allow-list of values.

## Acceptance Criteria

- [ ] `QueryTools` is decorated with `[McpServerToolType]` and auto-discovered by the MCP SDK.
- [ ] `project_outline` validates `path` and returns a structured markdown outline.
- [ ] `project_outline` with `group_by = "file"` groups symbols by file within directories.
- [ ] `project_outline` with `group_by = "kind"` groups symbols by `SymbolKind`.
- [ ] `project_outline` with `group_by = "directory"` produces a directory-focused summary.
- [ ] `project_outline` with `include_private = false` omits `Visibility.Private` symbols.
- [ ] `project_outline` with `include_private = true` includes all symbols.
- [ ] `project_outline` with `max_depth` limits directory traversal depth.
- [ ] `project_outline` produces ~3-8k tokens for a typical codebase (signatures only).
- [ ] `get_module_api` validates both `path` and `module_path`.
- [ ] `get_module_api` returns full API surface: symbols, signatures, parameters, return types, doc comments, dependencies.
- [ ] `get_module_api` with non-existent module returns structured error.
- [ ] `get_module_api` rejects `module_path` with traversal attempts.
- [ ] Invalid `group_by` value returns structured error.
- [ ] No tool echoes raw user-supplied input in response fields.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.
- [ ] Tool test coverage is 85%+.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/Tools/QueryTools.cs` | MCP tool class with `project_outline` and `get_module_api` |
| `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs` | Unit tests for both tools |

### Modify

None — `[McpServerToolType]` auto-discovery handles registration.

## Test Cases (`tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs`)

| Test | Description |
|---|---|
| ProjectOutline_ValidPath_ReturnsStructuredOutline | Valid project returns outline with file/symbol counts |
| ProjectOutline_GroupByFile_GroupsCorrectly | Symbols grouped under file headings within directories |
| ProjectOutline_GroupByKind_GroupsCorrectly | Symbols grouped by `SymbolKind` (Classes, Functions, Methods) |
| ProjectOutline_GroupByDirectory_SummarizesDirectories | Directory-level summary with file and symbol counts |
| ProjectOutline_InvalidGroupBy_ReturnsError | Unrecognized `group_by` value returns structured error |
| ProjectOutline_IncludePrivateFalse_OmitsPrivateSymbols | Private symbols excluded from output |
| ProjectOutline_IncludePrivateTrue_IncludesAllSymbols | Private symbols included in output |
| ProjectOutline_MaxDepth_LimitsTraversal | `max_depth = 1` shows only top-level directory contents |
| ProjectOutline_InvalidPath_ReturnsError | Path validation failure returns structured error |
| GetModuleApi_ValidModule_ReturnsFullApi | Returns symbols, signatures, parameters, return types, dependencies |
| GetModuleApi_NonExistentModule_ReturnsError | Non-existent module path returns `MODULE_NOT_FOUND` error |
| GetModuleApi_TraversalModulePath_ReturnsError | `module_path` with `..` is rejected |
| GetModuleApi_InvalidProjectPath_ReturnsError | Invalid `path` returns error |
| GetModuleApi_IncludesDependencies | Module's `require()` dependencies included in response |

All tests use **NSubstitute** to mock `SymbolStore` and `PathValidator`.

## Out of Scope

- `get_symbol`, `get_symbols` — covered in 08-002.
- `search_symbols`, `search_text` — covered in 08-003.
- Delta tools (`changes_since`, `file_tree`) — separate feature.
- Dependency tools (`dependency_graph`) — separate feature.
- Outline formatting customization beyond `group_by` and `max_depth`.
- Pagination of outline results.

## Notes / Decisions

1. **Token budget.** The outline format is deliberately compressed — signatures only, no function bodies, no inline documentation beyond what fits in a signature line. For a 100-file project with ~500 symbols, the outline should be ~4-6k tokens. This is the primary value proposition of CodeCompress.
2. **Markdown vs. JSON for outline.** The outline is returned as structured markdown (not raw JSON) because AI agents consume it as context, and markdown provides better information density per token than nested JSON. The `get_module_api` response uses JSON because it is structured data that agents may need to process programmatically.
3. **group_by validation.** Using an allow-list (`"file"`, `"kind"`, `"directory"`) rather than an enum parameter because MCP tool parameters are untyped strings from the agent. Invalid values return a structured error rather than silently defaulting.
4. **module_path as relative path.** Requiring a relative path (not absolute) prevents the agent from accessing files outside the project root. The tool combines `path + module_path` and validates the result via `PathValidator`.
5. **Snapshot testing.** Outline formatting is a good candidate for **Verify** snapshot tests in addition to assertion-based tests. Snapshot tests capture the exact output format and detect unintended formatting changes.
