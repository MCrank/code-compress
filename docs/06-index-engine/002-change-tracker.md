# 002 — ChangeTracker: Diff Logic for Incremental Indexing

## Summary

`ChangeTracker` compares current file hashes against previously stored hashes to classify every file as new, modified, deleted, or unchanged. This diff result (`ChangeSet`) drives the `IndexEngine`'s decision about which files to parse, update, or remove — enabling incremental indexing that skips unchanged files entirely.

## Dependencies

| Dependency | What it provides |
|------------|-----------------|
| Feature 01 (Project Scaffold) | Solution structure, `Directory.Build.props`, global analyzers |
| Feature 02 (Core Models & Interfaces) | Base model types, shared interfaces |

## Scope

### Production Code

**File:** `src/CodeCompress.Core/Indexing/ChangeTracker.cs`

#### `ChangeSet` Record

```csharp
public sealed record ChangeSet(
    IReadOnlyList<string> NewFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> UnchangedFiles);
```

- Immutable record type with `IReadOnlyList<string>` properties (file paths).
- Provides a convenience `bool HasChanges => NewFiles.Count > 0 || ModifiedFiles.Count > 0 || DeletedFiles.Count > 0;` property.

#### `IChangeTracker` Interface

```csharp
public interface IChangeTracker
{
    ChangeSet DetectChanges(
        Dictionary<string, string> currentHashes,
        Dictionary<string, string> storedHashes);
}
```

#### `ChangeTracker` Class

- **`DetectChanges(currentHashes, storedHashes) -> ChangeSet`**
  - Iterates `currentHashes`:
    - Key not in `storedHashes` --> **New**
    - Key in `storedHashes` but value differs --> **Modified**
    - Key in `storedHashes` with same value --> **Unchanged**
  - Iterates `storedHashes`:
    - Key not in `currentHashes` --> **Deleted**
  - Pure function — no side effects, no I/O, no async needed.
  - Uses `StringComparer.OrdinalIgnoreCase` for path keys (cross-platform safety).

### Test Code

**File:** `tests/CodeCompress.Core.Tests/Indexing/ChangeTrackerTests.cs`

| # | Test Case | Setup | Expected |
|---|-----------|-------|----------|
| 1 | First index — all new | current: 3 files, stored: empty | NewFiles=3, rest empty |
| 2 | Mix of all categories | current has new+modified+unchanged, stored has deleted+modified+unchanged | Correct counts in each bucket |
| 3 | No changes | current == stored (same keys & values) | UnchangedFiles = all, rest empty |
| 4 | All deleted | current: empty, stored: 3 files | DeletedFiles=3, rest empty |
| 5 | Empty project (current empty, stored empty) | Both dictionaries empty | All lists empty |
| 6 | First run (stored empty) | current: 5 files, stored: empty | NewFiles=5 |
| 7 | Single modified file | One path with different hash | ModifiedFiles=1, UnchangedFiles=rest |
| 8 | HasChanges true when changes exist | Any non-empty change bucket | `HasChanges` returns `true` |
| 9 | HasChanges false when no changes | All unchanged | `HasChanges` returns `false` |
| 10 | Case-insensitive path matching | Same path different casing | Treated as same file (unchanged, not new+deleted) |

All tests are synchronous (no async needed). TUnit assertion style throughout.

## Acceptance Criteria

- [ ] `ChangeSet` record defined with `NewFiles`, `ModifiedFiles`, `DeletedFiles`, `UnchangedFiles`
- [ ] `ChangeSet.HasChanges` convenience property works correctly
- [ ] `IChangeTracker` interface defined
- [ ] `ChangeTracker` implements `IChangeTracker`
- [ ] `DetectChanges` correctly classifies all four categories
- [ ] Path comparison is case-insensitive (`StringComparer.OrdinalIgnoreCase`)
- [ ] Pure function — no I/O, no side effects, no state mutation
- [ ] All 10 test cases pass
- [ ] Zero build warnings (SonarAnalyzer, nullable, code style)
- [ ] No `dynamic` or `object` types in public API

## Files to Create/Modify

| Action | File |
|--------|------|
| Create | `src/CodeCompress.Core/Indexing/ChangeTracker.cs` |
| Create | `src/CodeCompress.Core/Indexing/IChangeTracker.cs` |
| Create | `src/CodeCompress.Core/Indexing/ChangeSet.cs` |
| Create | `tests/CodeCompress.Core.Tests/Indexing/ChangeTrackerTests.cs` |

## Out of Scope

- Computing the hashes themselves (handled by `FileHasher` in 001)
- Loading stored hashes from SQLite (handled by `SymbolStore` in Feature 03)
- Acting on the `ChangeSet` (parsing, storing — handled by `IndexEngine` in 003)
- File discovery or directory walking

## Notes / Decisions

- **Synchronous by design:** `DetectChanges` is a pure in-memory comparison of two dictionaries — no reason for async. The `IndexEngine` will call it after awaiting hash results.
- **Case-insensitive paths:** Windows file systems are case-insensitive; macOS HFS+ is case-insensitive by default. Using `OrdinalIgnoreCase` avoids false positives (same file detected as both new and deleted due to casing).
- **Immutable output:** `ChangeSet` uses `IReadOnlyList<string>` to prevent callers from mutating the result.
- **No path normalization here:** Callers must pass already-normalized paths. Both `currentHashes` and `storedHashes` should use the same normalization scheme (enforced by `PathValidator` upstream).
