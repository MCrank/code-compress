# 001 — FileHasher: SHA-256 Parallel File Hashing

## Summary

`FileHasher` computes SHA-256 hashes of source files for change detection during incremental indexing. It uses `Parallel.ForEachAsync` for concurrent hashing and async file I/O throughout, with `ReadOnlyMemory<byte>` / buffer pooling for efficient memory use. An `IFileHasher` interface is provided for testability.

## Dependencies

| Dependency | What it provides |
|------------|-----------------|
| Feature 01 (Project Scaffold) | Solution structure, `Directory.Build.props`, global analyzers |
| Feature 02 (Core Models & Interfaces) | Base model types, shared interfaces |

## Scope

### Production Code

**File:** `src/CodeCompress.Core/Indexing/FileHasher.cs`

#### `IFileHasher` Interface

```csharp
public interface IFileHasher
{
    Task<string> HashFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> HashFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
```

#### `FileHasher` Class

- **`HashFileAsync(string filePath, CancellationToken) -> string`**
  - Opens file with async `FileStream` (read-only, sequential scan hint).
  - Computes SHA-256 using `System.Security.Cryptography.SHA256`.
  - Uses `ArrayPool<byte>` or `ReadOnlyMemory<byte>` buffering — no large allocations per file.
  - Returns lowercase hex-encoded hash string.
  - Throws `FileNotFoundException` for missing files.

- **`HashFilesAsync(IEnumerable<string> filePaths, CancellationToken) -> Dictionary<string, string>`**
  - Uses `Parallel.ForEachAsync` with a configurable `MaxDegreeOfParallelism` (default: `Environment.ProcessorCount`).
  - Collects results into a `ConcurrentDictionary<string, string>`, then returns as `Dictionary`.
  - Propagates cancellation via `CancellationToken`.
  - If any single file fails, the exception propagates (no partial results swallowed).

### Test Code

**File:** `tests/CodeCompress.Core.Tests/Indexing/FileHasherTests.cs`

| # | Test Case | Assertion |
|---|-----------|-----------|
| 1 | Hash a known file with known content | Returns expected SHA-256 hex string |
| 2 | Hash same content twice | Both hashes are identical |
| 3 | Hash different content | Hashes differ |
| 4 | Hash multiple files in parallel | All results correct and dictionary complete |
| 5 | Non-existent file path | Throws `FileNotFoundException` |
| 6 | Empty file (zero bytes) | Returns valid SHA-256 of empty input (`e3b0c44298fc...`) |
| 7 | Large file (>1 MB generated temp file) | Completes without error, returns valid hash |
| 8 | Cancellation token cancelled before start | Throws `OperationCanceledException` |
| 9 | Cancellation token cancelled mid-batch | Throws `OperationCanceledException`, does not hang |

All tests use temporary files created in a `[Before(Test)]` setup and cleaned up in `[After(Test)]`. TUnit assertion style (`await Assert.That(...)`) throughout.

## Acceptance Criteria

- [ ] `IFileHasher` interface defined with both methods
- [ ] `FileHasher` implements `IFileHasher`
- [ ] `HashFileAsync` returns correct lowercase hex SHA-256 for any file
- [ ] `HashFilesAsync` hashes files concurrently via `Parallel.ForEachAsync`
- [ ] No blocking I/O — all file access is async
- [ ] Memory-efficient: uses `ArrayPool<byte>` or pooled buffers, not unbounded `byte[]` allocations
- [ ] `CancellationToken` respected in both methods
- [ ] `FileNotFoundException` thrown for missing files
- [ ] All 9 test cases pass
- [ ] Zero build warnings (SonarAnalyzer, nullable, code style)
- [ ] No `dynamic` or `object` types in public API

## Files to Create/Modify

| Action | File |
|--------|------|
| Create | `src/CodeCompress.Core/Indexing/FileHasher.cs` |
| Create | `src/CodeCompress.Core/Indexing/IFileHasher.cs` |
| Create | `tests/CodeCompress.Core.Tests/Indexing/FileHasherTests.cs` |

## Out of Scope

- Directory walking / file discovery (handled by `IndexEngine` in 003)
- Storing hashes in SQLite (handled by `SymbolStore` in Feature 03)
- Path validation / security checks (handled by `PathValidator` in Feature 04)
- Incremental comparison logic (handled by `ChangeTracker` in 002)

## Notes / Decisions

- **Hash algorithm:** SHA-256 chosen for collision resistance and consistency with Git-style integrity checks. `System.Security.Cryptography.SHA256.HashDataAsync` (or `IncrementalHash`) preferred over `SHA256.Create()` for thread safety.
- **Buffer size:** 8 KB default buffer aligns with typical file system block size; can be tuned later.
- **Parallelism cap:** Defaults to `Environment.ProcessorCount` to avoid thread-pool starvation; the `IndexEngine` may override this later.
- **Path normalization:** `FileHasher` does NOT normalize or validate paths — callers (IndexEngine) must pass already-validated absolute paths.
