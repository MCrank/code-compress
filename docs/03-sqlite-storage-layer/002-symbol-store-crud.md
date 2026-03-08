# 03-002: SymbolStore Repository — CRUD Operations

## Summary

Implement the `SymbolStore` class as a repository-pattern data access layer over the SQLite database provisioned in 03-001. This class owns all insert, read, update, and delete operations for repositories, files, symbols, dependencies, and snapshots. Batch inserts use explicit transactions for performance. FTS5 virtual tables are kept in sync via SQLite triggers. All SQL is parameterized — zero string concatenation.

## Dependencies

- **03-001** — `SqliteConnectionFactory`, `Migrations`, schema must be in place
- **Feature 01** — Domain models (`Repository`, `FileRecord`, `Symbol`, `Dependency`, `IndexSnapshot`)
- **Feature 02** — Parser output models that feed into symbol/file inserts

## Scope

### SymbolStore (src/CodeCompress.Core/Storage/SymbolStore.cs)

Implements `ISymbolStore` interface. Receives `SqliteConnection` via constructor (injected by the connection factory or directly in tests).

#### Repository Operations

| Method | Signature | Behavior |
|--------|-----------|----------|
| `UpsertRepository` | `Task UpsertRepositoryAsync(Repository repo)` | INSERT OR REPLACE into `repositories`. Updates `last_indexed`, `file_count`, `symbol_count`. |
| `GetRepository` | `Task<Repository?> GetRepositoryAsync(string repoId)` | SELECT by primary key. Returns null if not found. |
| `DeleteRepository` | `Task DeleteRepositoryAsync(string repoId)` | DELETE from `repositories`. Cascading FKs remove child rows. |

#### File Operations

| Method | Signature | Behavior |
|--------|-----------|----------|
| `InsertFiles` | `Task InsertFilesAsync(IReadOnlyList<FileRecord> files)` | Batch INSERT within a transaction. Returns inserted row IDs. |
| `GetFilesByRepo` | `Task<IReadOnlyList<FileRecord>> GetFilesByRepoAsync(string repoId)` | SELECT all files for a repository. |
| `GetFileByPath` | `Task<FileRecord?> GetFileByPathAsync(string repoId, string relativePath)` | SELECT by unique index `(repo_id, relative_path)`. |
| `UpdateFile` | `Task UpdateFileAsync(FileRecord file)` | UPDATE by primary key — sets `content_hash`, `byte_length`, `line_count`, `last_modified`, `indexed_at`. |
| `DeleteFile` | `Task DeleteFileAsync(long fileId)` | DELETE by primary key. Cascading FKs remove child symbols and dependencies. |

#### Symbol Operations

| Method | Signature | Behavior |
|--------|-----------|----------|
| `InsertSymbols` | `Task InsertSymbolsAsync(IReadOnlyList<Symbol> symbols)` | Batch INSERT within a transaction. |
| `GetSymbolsByFile` | `Task<IReadOnlyList<Symbol>> GetSymbolsByFileAsync(long fileId)` | SELECT all symbols for a file, ordered by `line_start`. |
| `DeleteSymbolsByFile` | `Task DeleteSymbolsByFileAsync(long fileId)` | DELETE all symbols belonging to a file. |

#### Dependency Operations

| Method | Signature | Behavior |
|--------|-----------|----------|
| `InsertDependencies` | `Task InsertDependenciesAsync(IReadOnlyList<Dependency> deps)` | Batch INSERT within a transaction. |
| `GetDependenciesByFile` | `Task<IReadOnlyList<Dependency>> GetDependenciesByFileAsync(long fileId)` | SELECT all dependencies for a file. |
| `DeleteDependenciesByFile` | `Task DeleteDependenciesByFileAsync(long fileId)` | DELETE all dependencies belonging to a file. |

#### Snapshot Operations

| Method | Signature | Behavior |
|--------|-----------|----------|
| `CreateSnapshot` | `Task<long> CreateSnapshotAsync(IndexSnapshot snapshot)` | INSERT into `index_snapshots`. Returns new row ID. |
| `GetSnapshot` | `Task<IndexSnapshot?> GetSnapshotAsync(long snapshotId)` | SELECT by primary key. |
| `GetSnapshotsByRepo` | `Task<IReadOnlyList<IndexSnapshot>> GetSnapshotsByRepoAsync(string repoId)` | SELECT all snapshots for a repository, ordered by `created_at DESC`. |

### Batch Insert Strategy

- All batch methods (`InsertFilesAsync`, `InsertSymbolsAsync`, `InsertDependenciesAsync`) wrap operations in an explicit `BEGIN TRANSACTION` / `COMMIT`
- Use a single prepared `SqliteCommand` with parameters reset per row to minimize allocations
- On failure, `ROLLBACK` the transaction and let the exception propagate
- No partial inserts — all-or-nothing semantics

### FTS5 Synchronization

SQLite triggers maintain FTS5 tables automatically:

#### symbols_fts Triggers

```sql
-- After INSERT on symbols
CREATE TRIGGER IF NOT EXISTS symbols_ai AFTER INSERT ON symbols BEGIN
    INSERT INTO symbols_fts(rowid, name, signature, doc_comment)
    VALUES (new.id, new.name, new.signature, new.doc_comment);
END;

-- After DELETE on symbols
CREATE TRIGGER IF NOT EXISTS symbols_ad AFTER DELETE ON symbols BEGIN
    INSERT INTO symbols_fts(symbols_fts, rowid, name, signature, doc_comment)
    VALUES ('delete', old.id, old.name, old.signature, old.doc_comment);
END;

-- After UPDATE on symbols
CREATE TRIGGER IF NOT EXISTS symbols_au AFTER UPDATE ON symbols BEGIN
    INSERT INTO symbols_fts(symbols_fts, rowid, name, signature, doc_comment)
    VALUES ('delete', old.id, old.name, old.signature, old.doc_comment);
    INSERT INTO symbols_fts(rowid, name, signature, doc_comment)
    VALUES (new.id, new.name, new.signature, new.doc_comment);
END;
```

#### file_content_fts

- `file_content_fts` is a standalone (not content-synced) FTS5 table
- Managed explicitly by `SymbolStore` during file insert/update/delete rather than via triggers, because file content is not stored in the `files` table itself — it is read from disk and inserted directly into FTS5

### ISymbolStore Interface (src/CodeCompress.Core/Storage/ISymbolStore.cs)

Declares all methods listed above. Enables DI registration and NSubstitute mocking in higher-layer tests.

## Acceptance Criteria

- [ ] `UpsertRepositoryAsync` inserts a new repository and updates an existing one (verified by round-trip read)
- [ ] `DeleteRepositoryAsync` cascades to remove all child files, symbols, dependencies, and snapshots
- [ ] `InsertFilesAsync` inserts a batch of files in a single transaction
- [ ] `GetFileByPathAsync` returns the correct file by `(repo_id, relative_path)` and returns null for missing paths
- [ ] `InsertSymbolsAsync` inserts a batch of symbols; `GetSymbolsByFileAsync` retrieves them in `line_start` order
- [ ] `DeleteSymbolsByFileAsync` removes all symbols for a file and their FTS5 entries
- [ ] `InsertDependenciesAsync` inserts a batch of dependencies in a single transaction
- [ ] `CreateSnapshotAsync` returns the auto-incremented snapshot ID
- [ ] `GetSnapshotsByRepoAsync` returns snapshots ordered by `created_at DESC`
- [ ] Batch inserts roll back entirely on failure — no partial rows are committed
- [ ] FTS5 triggers keep `symbols_fts` in sync after insert, update, and delete on `symbols`
- [ ] `file_content_fts` is populated and cleaned up correctly during file insert/update/delete
- [ ] All SQL uses `@param` parameterized syntax — zero string concatenation
- [ ] No `dynamic` or `object` types in public API signatures
- [ ] All tests pass: `SymbolStoreCrudTests`

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `src/CodeCompress.Core/Storage/ISymbolStore.cs` | Repository interface |
| `src/CodeCompress.Core/Storage/SymbolStore.cs` | Full CRUD implementation |
| `tests/CodeCompress.Core.Tests/Storage/SymbolStoreCrudTests.cs` | CRUD operation tests |

### Modified Files

| File | Change |
|------|--------|
| `src/CodeCompress.Core/Storage/Migrations.cs` | Add FTS5 trigger DDL to the migration script |
| `src/CodeCompress.Core/DependencyInjection.cs` (or equivalent) | Register `ISymbolStore` as scoped/transient |

## Out of Scope

- FTS5 search queries and ranking (covered in 03-003)
- Project outline aggregation queries (covered in 03-003)
- Dependency graph traversal (covered in 03-003)
- FTS5 query sanitization (covered in 03-003)
- Index engine orchestration (Feature 04+)
- MCP tool routing (Feature 05+)

## Notes/Decisions

- **Prepared statement reuse:** For batch inserts, create one `SqliteCommand`, set its `CommandText` once, add parameters once, then loop over rows resetting parameter values. This avoids repeated parsing overhead.
- **CASCADE deletes:** The schema uses `ON DELETE CASCADE` on all foreign keys. Deleting a repository removes everything. Deleting a file removes its symbols and dependencies. This keeps cleanup logic out of application code.
- **FTS5 content-sync vs. standalone:** `symbols_fts` uses content-sync mode (`content=symbols`) to avoid duplicating data. `file_content_fts` is standalone because file content is not stored in any regular table — it is inserted directly into FTS5 from disk during indexing.
- **Transaction isolation:** Each batch method manages its own transaction. The caller (IndexEngine) is responsible for orchestrating the order of operations but does not need to manage transactions.
- **In-memory testing:** All tests use in-memory SQLite databases. `Migrations.ApplyAsync` is called in test setup to provision the schema before each test.
