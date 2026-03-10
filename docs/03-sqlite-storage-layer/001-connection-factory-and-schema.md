# 03-001: SqliteConnectionFactory and Schema Creation

## Summary

Establish the SQLite foundation for CodeCompress by implementing a connection factory that creates and configures per-repository databases, and a migrations system that idempotently provisions the full schema (tables, indexes, FTS5 virtual tables). This is the lowest layer of the storage stack — all subsequent storage work depends on it.

## Dependencies

- **Feature 01** — Project models and shared types (e.g., repository identity, symbol kinds)
- **Feature 02** — Language parser interfaces (symbol/file models that inform table shapes)

## Scope

### SqliteConnectionFactory (src/CodeCompress.Core/Storage/SqliteConnectionFactory.cs)

- Computes the database path: `~/.codecompress/{repo-hash}.db`
  - `repo-hash` = lowercase hex SHA-256 of the **normalized absolute path** to the project root (forward slashes, no trailing separator, invariant culture)
  - Directory `~/.codecompress/` is created if it does not exist
- Opens a `SqliteConnection` from `Microsoft.Data.Sqlite` with the computed path
- Applies connection-level PRAGMAs immediately after open:
  - `PRAGMA journal_mode=WAL;`
  - `PRAGMA synchronous=NORMAL;`
  - `PRAGMA foreign_keys=ON;`
- Exposes an async factory method: `Task<SqliteConnection> CreateConnectionAsync(string projectRootPath)`
- Validates that `projectRootPath` is a rooted, canonical path (uses `PathValidator`)
- Implements `IConnectionFactory` interface for DI registration and testability

### Migrations (src/CodeCompress.Core/Storage/Migrations.cs)

Creates all tables on first run. Must be idempotent (`CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS`).

#### Tables

| Table | Columns |
|-------|---------|
| **repositories** | `id TEXT PRIMARY KEY` (SHA-256 of root path), `root_path TEXT NOT NULL`, `name TEXT NOT NULL`, `language TEXT NOT NULL`, `last_indexed INTEGER NOT NULL`, `file_count INTEGER NOT NULL DEFAULT 0`, `symbol_count INTEGER NOT NULL DEFAULT 0` |
| **files** | `id INTEGER PRIMARY KEY AUTOINCREMENT`, `repo_id TEXT NOT NULL REFERENCES repositories(id) ON DELETE CASCADE`, `relative_path TEXT NOT NULL`, `content_hash TEXT NOT NULL`, `byte_length INTEGER NOT NULL`, `line_count INTEGER NOT NULL`, `last_modified INTEGER NOT NULL`, `indexed_at INTEGER NOT NULL` |
| **symbols** | `id INTEGER PRIMARY KEY AUTOINCREMENT`, `file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE`, `name TEXT NOT NULL`, `kind TEXT NOT NULL`, `signature TEXT NOT NULL`, `parent_symbol TEXT`, `byte_offset INTEGER NOT NULL`, `byte_length INTEGER NOT NULL`, `line_start INTEGER NOT NULL`, `line_end INTEGER NOT NULL`, `visibility TEXT NOT NULL`, `doc_comment TEXT` |
| **dependencies** | `id INTEGER PRIMARY KEY AUTOINCREMENT`, `file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE`, `requires_path TEXT NOT NULL`, `resolved_file_id INTEGER REFERENCES files(id) ON DELETE SET NULL`, `alias TEXT` |
| **index_snapshots** | `id INTEGER PRIMARY KEY AUTOINCREMENT`, `repo_id TEXT NOT NULL REFERENCES repositories(id) ON DELETE CASCADE`, `snapshot_label TEXT NOT NULL`, `created_at INTEGER NOT NULL`, `file_hashes TEXT NOT NULL` (JSON blob) |

#### Indexes

- `ix_files_repo_id` on `files(repo_id)`
- `ix_files_content_hash` on `files(content_hash)`
- `ix_files_repo_path` UNIQUE on `files(repo_id, relative_path)`
- `ix_symbols_file_id` on `symbols(file_id)`
- `ix_symbols_name` on `symbols(name)`
- `ix_symbols_kind` on `symbols(kind)`
- `ix_dependencies_file_id` on `dependencies(file_id)`
- `ix_dependencies_resolved` on `dependencies(resolved_file_id)`
- `ix_snapshots_repo_id` on `index_snapshots(repo_id)`

#### FTS5 Virtual Tables (created here, populated by SymbolStore in 03-002)

- `symbols_fts` — `CREATE VIRTUAL TABLE IF NOT EXISTS symbols_fts USING fts5(name, signature, doc_comment, content=symbols, content_rowid=id)`
- `file_content_fts` — `CREATE VIRTUAL TABLE IF NOT EXISTS file_content_fts USING fts5(relative_path, content)`

#### Migration Runner

- Static async method: `Task ApplyAsync(SqliteConnection connection)`
- Executes all DDL in a single transaction
- Safe to call on every connection open (idempotent)

## Acceptance Criteria

- [ ] `SqliteConnectionFactory` computes the correct database path for a given project root
- [ ] Database directory (`~/.codecompress/`) is created automatically if missing
- [ ] Opened connections have WAL mode enabled (verified via `PRAGMA journal_mode` query)
- [ ] Opened connections have `synchronous=NORMAL` (verified via `PRAGMA synchronous` query)
- [ ] Opened connections have `foreign_keys=ON` (verified via `PRAGMA foreign_keys` query)
- [ ] All six tables exist after migration (repositories, files, symbols, dependencies, index_snapshots)
- [ ] All indexes exist after migration
- [ ] FTS5 virtual tables (symbols_fts, file_content_fts) exist after migration
- [ ] Migrations are idempotent — running twice produces no errors and no duplicate objects
- [ ] `PathValidator` rejects non-rooted paths and paths containing `..` traversal
- [ ] Repo hash is deterministic — same path always produces the same hash
- [ ] Repo hash normalizes path separators (forward slashes) and removes trailing separators before hashing
- [ ] All SQL uses parameterized queries or literal DDL — zero string concatenation with user input
- [ ] All tests pass: `SqliteConnectionFactoryTests`, `MigrationsTests`

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `src/CodeCompress.Core/Storage/IConnectionFactory.cs` | Interface for DI |
| `src/CodeCompress.Core/Storage/SqliteConnectionFactory.cs` | Connection factory implementation |
| `src/CodeCompress.Core/Storage/Migrations.cs` | Schema creation and idempotent DDL |
| `tests/CodeCompress.Core.Tests/Storage/SqliteConnectionFactoryTests.cs` | Factory unit tests |
| `tests/CodeCompress.Core.Tests/Storage/MigrationsTests.cs` | Schema verification tests |

### Modified Files

| File | Change |
|------|--------|
| `src/CodeCompress.Core/DependencyInjection.cs` (or equivalent) | Register `IConnectionFactory` as singleton |

## Out of Scope

- CRUD operations on any table (covered in 03-002)
- FTS5 data population and triggers (covered in 03-002)
- Query methods, search, and aggregation (covered in 03-003)
- Database backup, export, or compaction
- Multi-database or cross-repository queries
- Connection pooling beyond what `Microsoft.Data.Sqlite` provides by default

## Notes/Decisions

- **In-memory databases for tests:** Test classes should use `SqliteConnection("DataSource=:memory:")` to avoid filesystem side effects. The factory can be bypassed in tests by injecting an already-opened in-memory connection.
- **SHA-256 for repo hash:** Using the full 64-character hex digest avoids collisions and aligns with the content-hash strategy used for files.
- **INTEGER for timestamps:** All timestamp columns store Unix epoch seconds as INTEGER, consistent with SQLite best practices and avoiding datetime parsing overhead.
- **JSON blob for snapshot hashes:** `file_hashes` in `index_snapshots` stores a JSON dictionary of `{ relative_path: content_hash }`. This avoids a separate join table and keeps snapshot diffing simple.
- **FTS5 content-sync tables:** `symbols_fts` uses `content=symbols, content_rowid=id` so it references the main symbols table without duplicating storage. Triggers or manual rebuild commands will be added in 03-002.
