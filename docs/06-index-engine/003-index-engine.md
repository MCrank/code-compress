# 003 — IndexEngine: Orchestrates Full and Incremental Indexing

## Summary

`IndexEngine` is the singleton orchestration service that ties together `FileHasher`, `ChangeTracker`, language parsers, and `SymbolStore` to perform full and incremental indexing of a codebase. It discovers source files, hashes them, detects changes, dispatches parsing to the correct `ILanguageParser` by file extension, and persists results to SQLite — all with parallel execution and cancellation support.

## Dependencies

| Dependency | What it provides |
|------------|-----------------|
| Feature 03 (SQLite Storage) | `SymbolStore` / `ISymbolStore` — persistent hash and symbol storage |
| Feature 04 (Path Validation) | `PathValidator` — canonicalization and traversal prevention |
| Feature 05 (Luau Parser) | `LuauParser : ILanguageParser` — first concrete parser for testing |
| 06-001 (FileHasher) | `IFileHasher` — parallel SHA-256 hashing |
| 06-002 (ChangeTracker) | `IChangeTracker` — diff logic producing `ChangeSet` |

## Scope

### Production Code

**File:** `src/CodeCompress.Core/Indexing/IndexEngine.cs`

#### `IndexResult` Record

```csharp
public sealed record IndexResult(
    int FilesIndexed,
    int FilesSkipped,
    int FilesDeleted,
    int SymbolsFound,
    long DurationMs);
```

#### `IIndexEngine` Interface

```csharp
public interface IIndexEngine
{
    Task<IndexResult> IndexProjectAsync(
        string projectRoot,
        string? language = null,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        CancellationToken cancellationToken = default);
}
```

#### `IndexEngine` Class

**Constructor injection:**
- `IFileHasher` — for hashing discovered files
- `IChangeTracker` — for diffing current vs. stored hashes
- `IEnumerable<ILanguageParser>` — all registered language parsers (resolved via DI)
- `ISymbolStore` (or `SymbolStore`) — for reading stored hashes and writing parse results
- `IPathValidator` — for path canonicalization and security checks
- `ILogger<IndexEngine>` — structured logging

**Initialization (constructor or lazy init):**
- Builds a `Dictionary<string, ILanguageParser>` mapping file extensions (e.g., `.luau`, `.lua`, `.cs`) to their parser, sourced from each parser's `FileExtensions` property.

**`IndexProjectAsync` — Step-by-Step Flow:**

1. **Validate project root** via `IPathValidator`. Reject if path is invalid or does not exist.
2. **Discover source files:**
   - Recursively walk `projectRoot` using `Directory.EnumerateFiles` with `SearchOption.AllDirectories`.
   - Apply default excludes: `.git/`, `node_modules/`, `bin/`, `obj/`, `Packages/`, `build/`, `*.rbxlx`, `*.rbxl`.
   - Apply caller-supplied `excludePatterns` (glob-style, matched against relative paths).
   - Apply caller-supplied `includePatterns` (if provided, only matching files are kept).
   - If `language` parameter is set, filter to only file extensions registered by the matching parser's `LanguageId`.
   - Skip files whose extensions have no registered parser.
3. **Hash all discovered files** in parallel via `IFileHasher.HashFilesAsync`.
4. **Load stored hashes** from `ISymbolStore` for this project root.
5. **Detect changes** via `IChangeTracker.DetectChanges(currentHashes, storedHashes)`.
6. **If no changes:** return early with `FilesIndexed=0, FilesSkipped=allFiles.Count`.
7. **Parse new and modified files** via `Parallel.ForEachAsync`:
   - Resolve the correct `ILanguageParser` from the extension map.
   - Call `parser.ParseAsync(filePath, cancellationToken)`.
   - Collect all `Symbol` results.
8. **Update `ISymbolStore` in a single transaction:**
   - Insert symbols for new files.
   - Replace symbols for modified files (delete old, insert new).
   - Delete symbols and file records for deleted files.
   - Update stored file hashes.
9. **Update repository metadata** (last indexed timestamp, file count, symbol count).
10. **Return `IndexResult`** with aggregate stats and elapsed time.

**Default exclude list (constant):**

```csharp
private static readonly string[] DefaultExcludes =
[
    ".git", "node_modules", "bin", "obj",
    "Packages", "build", ".vs", ".idea"
];

private static readonly string[] DefaultExcludeExtensions =
[
    ".rbxlx", ".rbxl"
];
```

**Pattern matching:**
- Glob patterns (`*`, `**`, `?`) for include/exclude, matched against paths relative to `projectRoot`.
- Use `Microsoft.Extensions.FileSystemGlobbing` (already in the .NET SDK) for glob evaluation.

### Test Code

**File:** `tests/CodeCompress.Core.Tests/Indexing/IndexEngineTests.cs`

All dependencies are mocked via **NSubstitute**. No real file system or SQLite access.

| # | Test Case | Mocked Setup | Expected |
|---|-----------|-------------|----------|
| 1 | Full index of new project | ChangeTracker returns all NewFiles, no stored hashes | All files parsed, symbols stored, `FilesIndexed` = file count |
| 2 | Incremental — one modified file | ChangeTracker returns 1 Modified, rest Unchanged | Only 1 file re-parsed, `FilesIndexed=1`, `FilesSkipped=rest` |
| 3 | Incremental — no changes | ChangeTracker returns all Unchanged | Zero files parsed, `FilesIndexed=0`, `FilesSkipped=all` |
| 4 | Deleted file | ChangeTracker returns 1 Deleted | Symbols removed from store, `FilesDeleted=1` |
| 5 | Language filter — Luau only | `language="luau"`, project has .luau and .cs files | Only .luau files discovered and indexed |
| 6 | Exclude patterns respected | Exclude `**/tests/**` | Files under tests/ not discovered |
| 7 | Include patterns respected | Include `src/**/*.luau` | Only matching files discovered |
| 8 | Unknown file extensions skipped | Project has .txt files, no parser registered | .txt files excluded, no errors |
| 9 | Mixed-language project | .luau and .cs files, both parsers registered | Each file dispatched to correct parser |
| 10 | Default excludes applied | Project tree includes .git/, node_modules/, bin/ | Those directories not walked |
| 11 | Invalid project root | Path fails validation | Throws appropriate exception |
| 12 | Cancellation during hashing | Token cancelled mid-hash | `OperationCanceledException` thrown |
| 13 | Parser failure for one file | Parser throws for one file | Error logged, other files still indexed (or configurable: fail-fast) |

## Acceptance Criteria

- [ ] `IIndexEngine` interface defined with `IndexProjectAsync`
- [ ] `IndexEngine` implements `IIndexEngine` as a singleton service
- [ ] `IndexResult` record defined with all five properties
- [ ] Extension-to-parser lookup map built from injected `IEnumerable<ILanguageParser>`
- [ ] File discovery walks directory recursively, applies default and custom excludes
- [ ] Include patterns filter discovered files when provided
- [ ] Language filter restricts indexing to matching parser's extensions
- [ ] Files hashed in parallel via `IFileHasher`
- [ ] Changes detected via `IChangeTracker`
- [ ] Early return when `ChangeSet.HasChanges` is false
- [ ] New/modified files parsed in parallel via `Parallel.ForEachAsync`
- [ ] Correct parser selected per file extension
- [ ] `ISymbolStore` updated in a single transaction (insert new, replace modified, delete removed)
- [ ] Repository metadata updated after indexing
- [ ] `CancellationToken` propagated to all async operations
- [ ] All 13 test cases pass
- [ ] 90%+ code coverage on `IndexEngine`
- [ ] Zero build warnings (SonarAnalyzer, nullable, code style)
- [ ] No `dynamic` or `object` types in public API

## Files to Create/Modify

| Action | File |
|--------|------|
| Create | `src/CodeCompress.Core/Indexing/IndexEngine.cs` |
| Create | `src/CodeCompress.Core/Indexing/IIndexEngine.cs` |
| Create | `src/CodeCompress.Core/Indexing/IndexResult.cs` |
| Create | `tests/CodeCompress.Core.Tests/Indexing/IndexEngineTests.cs` |
| Modify | `src/CodeCompress.Core/DependencyInjection.cs` (register `IndexEngine` as singleton) |

## Out of Scope

- MCP tool wiring (`index_project` tool) — handled in Feature 07
- Snapshot creation / labeling — handled in Feature 07
- Cache invalidation tool — handled in Feature 07
- C# parser implementation — handled in Feature 12
- Real integration tests with file system + SQLite — handled in Feature 11
- Progress reporting / streaming updates to the MCP client

## Notes / Decisions

- **Singleton lifetime:** `IndexEngine` is registered as singleton because it holds the extension-to-parser map and is stateless otherwise. All mutable state lives in `SymbolStore` (scoped per database).
- **Error handling strategy:** A single file failing to parse should NOT abort the entire index operation. Log the error, skip the file, and continue. The `IndexResult` could include an `Errors` count or list in a future iteration, but for now logging is sufficient.
- **Glob matching:** `Microsoft.Extensions.FileSystemGlobbing.Matcher` is part of the .NET SDK and provides battle-tested glob support. No external dependency needed.
- **Transaction scope:** All store mutations (inserts, updates, deletes) for a single `IndexProjectAsync` call are wrapped in one SQLite transaction for atomicity and performance.
- **Parallelism:** `Parallel.ForEachAsync` is used for both hashing (in `FileHasher`) and parsing (in `IndexEngine`). The degree of parallelism defaults to `Environment.ProcessorCount` but could be made configurable via options in the future.
- **Testability:** Because every dependency is behind an interface and injected, the `IndexEngine` tests are pure unit tests with no file system or database access. Integration tests (Feature 11) will exercise the real stack.
