# 002 — QueryTools: get_symbol and get_symbols (Byte-Offset Retrieval)

## Summary

Implement the symbol retrieval tools that use byte-offset seeking to extract the exact source code of specific symbols from source files. These tools are the surgical alternative to loading entire files — the AI agent requests symbols by qualified name, and the tool reads only the precise bytes needed from disk. `get_symbols` provides batch retrieval for efficiency, and both tools handle partial failures gracefully.

## Dependencies

- **Feature 08-001** — `QueryTools` class and `project_outline`/`get_module_api` tools (same class file).
- **Feature 03** — `SymbolStore` for looking up symbol metadata (byte offsets, file paths).
- **Feature 04** — `PathValidator` for input sanitization.

## Scope

### 1. get_symbol Tool (added to `src/CodeCompress.Server/Tools/QueryTools.cs`)

```csharp
[McpServerTool(Name = "get_symbol")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `symbol_name` | `string` | Yes | — | Fully qualified symbol name (e.g., `"CombatService:ProcessAttack"`) |
| `include_context` | `bool` | No | `false` | Include 5 lines before/after the symbol |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Look up the symbol in `SymbolStore` by qualified name: `SymbolStore.GetSymbolAsync(repoId, symbolName, cancellationToken)`.
3. If symbol not found, return structured error: `{ "error": "Symbol not found", "code": "SYMBOL_NOT_FOUND", "symbol": "<sanitized_name>" }`.
4. Construct the full file path from the project root and the symbol's stored relative file path. Validate the resolved path via `PathValidator`.
5. Open the source file with `FileStream` using async I/O:
   - Seek to `ByteOffset`.
   - Read exactly `ByteLength` bytes.
   - Decode as UTF-8.
6. If `include_context = true`:
   - Read additional lines: 5 lines before `LineStart` and 5 lines after `LineEnd`.
   - Use line-based reading for context (seek to approximate position, then scan for line boundaries).
7. Return structured result:

```json
{
  "name": "ProcessAttack",
  "kind": "method",
  "parent": "CombatService",
  "file": "src/services/CombatService.luau",
  "line_start": 8,
  "line_end": 32,
  "signature": "function CombatService:ProcessAttack(attacker: Player, target: Player): DamageResult",
  "source_code": "function CombatService:ProcessAttack(attacker, target)\n  -- full body here\nend"
}
```

8. Source code content comes from source files and is **untrusted**. It is returned as a structured JSON field (`source_code`), never embedded in freeform prose.

### 2. get_symbols Tool (added to `src/CodeCompress.Server/Tools/QueryTools.cs`)

```csharp
[McpServerTool(Name = "get_symbols")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `symbol_names` | `string[]` | Yes | — | Array of fully qualified symbol names |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Validate `symbol_names` is not empty and does not exceed a reasonable limit (e.g., 50 symbols per call).
3. Look up all symbols in `SymbolStore` in a single batch query: `SymbolStore.GetSymbolsAsync(repoId, symbolNames, cancellationToken)`.
4. For each found symbol, read source code from file using byte-offset seeking (same logic as `get_symbol`).
5. Group file reads — if multiple symbols are in the same file, open the file once and seek to each offset.
6. Handle partial failures:
   - Symbols found: return full result objects.
   - Symbols not found: return error entries in a separate `errors` array.

```json
{
  "results": [
    {
      "name": "ProcessAttack",
      "kind": "method",
      "parent": "CombatService",
      "file": "src/services/CombatService.luau",
      "line_start": 8,
      "line_end": 32,
      "signature": "...",
      "source_code": "..."
    }
  ],
  "errors": [
    {
      "symbol": "NonExistentSymbol",
      "error": "Symbol not found",
      "code": "SYMBOL_NOT_FOUND"
    }
  ]
}
```

### 3. File Reading Strategy

| Concern | Approach |
|---|---|
| File access | `FileStream` with `FileOptions.Asynchronous` and `FileOptions.SequentialScan` |
| Seeking | `stream.Seek(byteOffset, SeekOrigin.Begin)` |
| Reading | Read exactly `byteLength` bytes into a buffer |
| Decoding | `Encoding.UTF8.GetString(buffer)` |
| Context lines | For `include_context`, read a larger region and trim to line boundaries |
| Batch optimization | For `get_symbols`, group symbols by file to minimize file opens |
| Read-only | Files are opened read-only with `FileAccess.Read` and `FileShare.Read` |

### 4. Security Considerations

- `symbol_name` is untrusted input — used only as a lookup key in parameterized SQL queries, never interpolated into SQL or file paths.
- Source file contents are untrusted (could contain prompt injection). Returned as a structured `source_code` field.
- File paths resolved from `SymbolStore` are re-validated via `PathValidator` before file access to prevent stored-path injection.
- `symbol_names` array size is limited to prevent resource exhaustion.

## Acceptance Criteria

- [ ] `get_symbol` validates `path` and looks up the symbol by qualified name.
- [ ] `get_symbol` reads source code from file using byte-offset seeking (not loading the entire file).
- [ ] `get_symbol` returns correct `name`, `kind`, `file`, `line_start`, `line_end`, `signature`, and `source_code`.
- [ ] `get_symbol` with `include_context = true` includes 5 lines before and after the symbol.
- [ ] `get_symbol` with non-existent symbol returns `SYMBOL_NOT_FOUND` error.
- [ ] `get_symbols` performs batch retrieval of multiple symbols.
- [ ] `get_symbols` returns partial results when some symbols are found and others are not.
- [ ] `get_symbols` groups file reads for symbols in the same file.
- [ ] `get_symbols` rejects requests exceeding the symbol limit (50).
- [ ] Byte offsets correctly map to the expected source code content.
- [ ] Path validation applies to both the project root and resolved file paths.
- [ ] Source code is returned as a structured field, not embedded in freeform text.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.

## Files to Create/Modify

### Create

None — tests are added to the existing test file.

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Server/Tools/QueryTools.cs` | Add `get_symbol` and `get_symbols` tool methods |
| `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs` | Add unit tests for both tools |

## Test Cases (added to `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs`)

| Test | Description |
|---|---|
| GetSymbol_ExistingSymbol_ReturnsSourceCode | Qualified name lookup returns correct source code and metadata |
| GetSymbol_WithContext_IncludesSurroundingLines | `include_context = true` adds 5 lines before and after |
| GetSymbol_WithContext_AtFileStart_HandlesGracefully | Symbol at line 1 — context does not go to negative line numbers |
| GetSymbol_WithContext_AtFileEnd_HandlesGracefully | Symbol at end of file — context does not exceed file bounds |
| GetSymbol_NonExistent_ReturnsError | Unknown symbol name returns `SYMBOL_NOT_FOUND` |
| GetSymbol_InvalidPath_ReturnsError | Path validation failure returns structured error |
| GetSymbol_ByteOffset_MatchesFileContent | Retrieved bytes match the expected source code at the stored offset |
| GetSymbols_AllFound_ReturnsAllResults | Batch of 3 symbols all found — 3 results, 0 errors |
| GetSymbols_SomeMissing_ReturnsPartialResults | 2 found, 1 missing — 2 results, 1 error |
| GetSymbols_NoneFound_ReturnsAllErrors | All symbols missing — 0 results, N errors |
| GetSymbols_SameFile_GroupsReads | Multiple symbols from same file — file opened once (verify via mock) |
| GetSymbols_ExceedsLimit_ReturnsError | More than 50 symbols requested — rejected |
| GetSymbols_EmptyArray_ReturnsError | Empty `symbol_names` array — rejected |
| GetSymbols_InvalidPath_ReturnsError | Path validation failure returns structured error |

Tests use **NSubstitute** to mock `SymbolStore` and `PathValidator`. File reading tests use a temporary file with known content to verify byte-offset accuracy.

## Out of Scope

- `search_symbols`, `search_text` — covered in 08-003.
- Caching of file contents in memory — files are read on demand.
- Streaming large symbol bodies — symbols are returned as complete strings.
- Decompilation or source generation — only reads existing source files.

## Notes / Decisions

1. **Byte-offset seeking vs. line-based reading.** Byte-offset seeking is the primary strategy because it reads exactly the bytes needed without scanning the entire file. The parser already computes `ByteOffset` and `ByteLength` during indexing, so this data is available in `SymbolStore`. This is critical for large files where a single function might be 50 lines in a 5000-line file.
2. **Context line implementation.** For `include_context`, the tool cannot simply seek to `ByteOffset - N` because it does not know where line boundaries are. The strategy is: seek backward from `ByteOffset` to find 5 line-break characters, and read forward from `ByteOffset + ByteLength` until 5 line-break characters are found. This is a bounded scan, not a full-file read.
3. **Batch optimization.** `get_symbols` groups requested symbols by file path before reading. This avoids opening the same file multiple times when an agent requests several symbols from the same module, which is the common case.
4. **Symbol name qualification.** Symbol names use the format `ClassName:MethodName` for methods and plain `FunctionName` for top-level functions. This matches the Luau naming convention and is unambiguous within a project.
5. **File change detection.** If the file has changed since indexing (byte offsets may be stale), the tool returns the stored code region without validation. A future enhancement could verify the content hash and suggest re-indexing.
