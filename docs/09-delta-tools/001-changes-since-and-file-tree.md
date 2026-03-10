# 001 — DeltaTools: changes_since and file_tree

## Summary

Change tracking tools that show what changed since a snapshot and provide an annotated file tree. The `changes_since` tool compares the current state of a project against a previously-created snapshot to produce a structured delta report (new/modified/deleted files and symbol-level diffs). The `file_tree` tool walks the project directory and returns an annotated tree with file counts and line counts per directory, respecting default exclusion patterns.

## Dependencies

- **Feature 07** — MCP server host, DI, `[McpServerToolType]` infrastructure.
- **Feature 03** — `SymbolStore` snapshot persistence and snapshot retrieval queries.
- **Feature 06** — `IndexEngine` for current file hashes and symbol data.
- **Feature 04** — `PathValidator` for input sanitization.

## Scope

### 1. DeltaTools Class (`src/CodeCompress.Server/Tools/DeltaTools.cs`)

A `[McpServerToolType]` class containing two `[McpServerTool]` methods. Receives `IndexEngine`, `SymbolStore`, and `PathValidator` via constructor injection.

### 2. changes_since Tool

| Aspect | Detail |
|---|---|
| Parameters | `path` (string, required) — project root; `snapshot_label` (string, required) — label of the snapshot to diff against |
| Path validation | `PathValidator.Validate(path)` — reject traversal attempts |
| Label sanitization | Strip or escape any characters that could be interpreted as agent instructions; reject labels exceeding 256 characters |
| Snapshot lookup | `SymbolStore.GetSnapshot(repoId, snapshotLabel)` — returns file hashes and symbol data at snapshot time |
| Current state | Load current file hashes from the indexed repository via `SymbolStore` |
| Diff logic | Compare snapshot file hashes against current hashes to classify files as new, modified, or deleted |
| Symbol-level diff | For modified files: compare old vs new symbol lists by name+kind to identify added, modified (signature changed), and removed symbols |
| Output format | Structured text with token-efficient layout (target ~1-3k tokens for a typical delta): |

```
Changes since snapshot "{label}":

New files (N):
  path/to/file.luau — 5 symbols

Modified files (N):
  path/to/other.luau
    + addedFunction(param: Type): ReturnType
    ~ modifiedFunction(param: Type): NewReturnType
    - removedFunction()

Deleted files (N):
  path/to/gone.luau

Summary: +X added, ~Y modified, -Z removed symbols
```

| Error handling | Non-existent snapshot returns a clear error message with available snapshot labels |

### 3. file_tree Tool

| Aspect | Detail |
|---|---|
| Parameters | `path` (string, required) — project root; `max_depth` (int, default 5) — maximum directory depth |
| Path validation | `PathValidator.Validate(path)` — reject traversal attempts |
| Directory walk | Recursive traversal of project directory up to `max_depth` |
| Default excludes | `.git/`, `node_modules/`, `bin/`, `obj/`, `.vs/`, `.idea/`, `Packages/`, `__pycache__/` |
| Annotations | Each directory shows file count and aggregate line count; each file shows line count |
| Output format | Indented tree structure: |

```
src/ (42 files, 3,210 lines)
  server/ (28 files, 2,100 lines)
    Services/ (8 files, 890 lines)
      CombatService.luau (120 lines)
      AIService.luau (95 lines)
    init.server.luau (45 lines)
  shared/ (14 files, 1,110 lines)
    Types/ (3 files, 210 lines)
```

### 4. Security

- All path parameters validated via `PathValidator` before any file system access.
- Snapshot labels sanitized — no raw user input echoed into output without escaping.
- Output is structured data only — no freeform text fields that could carry prompt injection.
- `max_depth` clamped to a reasonable range (1-20) to prevent excessive traversal.

### 5. Tests (`tests/CodeCompress.Server.Tests/Tools/DeltaToolsTests.cs`)

| Test | Description |
|---|---|
| ChangesSince_NewFiles_ReportsCorrectly | Add new files after snapshot, verify they appear in "New files" section with symbol counts |
| ChangesSince_ModifiedFiles_ShowsSymbolDiffs | Modify a file's symbols after snapshot, verify added/modified/removed symbols listed |
| ChangesSince_DeletedFiles_ReportsCorrectly | Delete files after snapshot, verify they appear in "Deleted files" section |
| ChangesSince_NoChanges_ReturnsEmptyDiff | No changes since snapshot, verify clean empty diff report |
| ChangesSince_NonExistentSnapshot_ReturnsError | Request a snapshot that does not exist, verify error with available labels |
| ChangesSince_InvalidPath_RejectsTraversal | Path with `..` or outside project root is rejected |
| ChangesSince_SnapshotLabelSanitized | Labels with special characters are sanitized in output |
| FileTree_RespectsMaxDepth | Set max_depth=2, verify deeper directories are not shown |
| FileTree_ExcludesDefaultPatterns | Verify `.git/`, `node_modules/`, etc. are excluded |
| FileTree_AnnotatesWithCounts | Verify file and line counts appear on directories and files |
| FileTree_EmptyDirectory_HandledGracefully | Empty project directory returns a minimal tree |
| FileTree_InvalidPath_RejectsTraversal | Path validation rejects traversal attempts |
| FileTree_MaxDepthClamped | Negative or excessive max_depth values are clamped |

## Acceptance Criteria

- [ ] `DeltaTools` class is annotated with `[McpServerToolType]` and auto-discovered by the MCP server.
- [ ] `changes_since` compares snapshot state against current index and returns a structured delta report.
- [ ] Modified files include symbol-level diffs (added/modified/removed symbols with signatures).
- [ ] Non-existent snapshots return a clear error with available snapshot labels.
- [ ] `file_tree` returns an annotated directory tree respecting `max_depth` and default excludes.
- [ ] All path parameters are validated via `PathValidator`.
- [ ] Snapshot labels are sanitized before being included in output.
- [ ] Output contains structured data only — no raw user input echoed without escaping.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.
- [ ] 85%+ code coverage for `DeltaTools`.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/Tools/DeltaTools.cs` | `[McpServerToolType]` class with `changes_since` and `file_tree` tools |
| `tests/CodeCompress.Server.Tests/Tools/DeltaToolsTests.cs` | Unit tests for both tools |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Core/Storage/SymbolStore.cs` | Add `GetSnapshot` method if not already present; add query for snapshot file hashes |

## Out of Scope

- Watching for file system changes in real time (inotify/fswatch) — changes are detected at query time.
- Custom exclude patterns — only default excludes for MVP.
- Streaming output for large trees — full tree is returned in one response.
- Binary file detection — all files in the tree are counted regardless of type.

## Notes / Decisions

1. **Symbol-level diffs.** Comparing symbols by `Name + Kind` pair. If a symbol's signature changes but its name and kind remain the same, it is classified as "modified." If a new name+kind appears, it is "added." If one disappears, it is "removed." This is a heuristic — renames will appear as remove+add.
2. **Token budget.** The `changes_since` output is designed to be compact (~1-3k tokens for a typical delta) so AI agents can consume it without excessive context usage. Full source code of changed functions is not included — agents should follow up with `get_symbol` for details.
3. **Default excludes.** The exclusion list is hardcoded for MVP. A future enhancement could allow per-project `.codecompressignore` files.
4. **Line counting in file_tree.** Line counts are computed by counting newline characters in each file. Binary files may produce inaccurate counts, but this is acceptable for the annotated tree use case.
