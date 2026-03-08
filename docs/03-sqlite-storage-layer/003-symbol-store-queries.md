# 03-003: SymbolStore Queries — FTS5 Search, Outlines, Dependency Graphs

## Summary

Extend `SymbolStore` with advanced query methods that power the MCP query and delta tools: FTS5 full-text search over symbols and file content, project outline aggregation, exact and batch symbol lookups, module API extraction, recursive dependency graph traversal, and snapshot-based change detection. All FTS5 queries are sanitized to prevent syntax injection.

## Dependencies

- **03-002** — `SymbolStore` CRUD operations, FTS5 triggers, and `ISymbolStore` interface
- **03-001** — Schema (tables, indexes, FTS5 virtual tables) must be provisioned

## Scope

### FTS5 Query Sanitization (src/CodeCompress.Core/Storage/Fts5Sanitizer.cs)

A dedicated static utility class for sanitizing user-supplied FTS5 query strings before they reach SQLite.

- Strip or escape FTS5 special syntax characters: `*`, `"`, `(`, `)`, `+`, `-`, `^`, `NEAR`, `AND`, `OR`, `NOT`, column filters (`:`)
- Convert the sanitized input into a safe quoted phrase or set of quoted terms
- Method signature: `static string Sanitize(string rawQuery)`
- Returns an empty string for null/whitespace input (caller skips the query)
- Never throws — always returns a safe string

### Query Methods (added to SymbolStore / ISymbolStore)

#### Symbol Search

| Method | Signature | Behavior |
|--------|-----------|----------|
| `SearchSymbols` | `Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(string repoId, string query, string? kind, int limit)` | FTS5 MATCH on `symbols_fts`, joined back to `symbols` and `files` for metadata. Optional filter by `kind` (function, class, etc.). Results ranked by FTS5 `bm25()`. Limited to `limit` rows (capped at a server-side max, e.g., 100). |
| `SearchText` | `Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(string repoId, string query, string? glob, int limit)` | FTS5 MATCH on `file_content_fts`. Optional glob filter on `relative_path` (e.g., `src/**/*.luau`). Returns file path, matching snippet via `snippet()`, and rank. Limited to `limit` rows. |

#### Exact and Batch Lookups

| Method | Signature | Behavior |
|--------|-----------|----------|
| `GetSymbolByName` | `Task<Symbol?> GetSymbolByNameAsync(string repoId, string symbolName)` | Exact match on `symbols.name` joined to `files.repo_id`. If `symbolName` contains `.` or `:`, treat as qualified name and match against `parent_symbol.name` pattern. Returns null if not found. |
| `GetSymbolsByNames` | `Task<IReadOnlyList<Symbol>> GetSymbolsByNamesAsync(string repoId, IReadOnlyList<string> symbolNames)` | Batch lookup using `WHERE name IN (...)` with parameterized placeholders. Returns all matches (may be fewer than requested if some names do not exist). |

#### Project Outline

| Method | Signature | Behavior |
|--------|-----------|----------|
| `GetProjectOutline` | `Task<ProjectOutline> GetProjectOutlineAsync(string repoId, bool includePrivate, string groupBy, int maxDepth)` | Aggregation query that returns a hierarchical view of the repository. `groupBy` controls grouping: `"file"` groups symbols under their file path, `"kind"` groups by symbol kind. `maxDepth` limits nesting for large projects. `includePrivate` controls whether symbols with `visibility = "private"` are included. Returns a structured `ProjectOutline` model (not raw SQL rows). |

#### Module API

| Method | Signature | Behavior |
|--------|-----------|----------|
| `GetModuleApi` | `Task<ModuleApi> GetModuleApiAsync(string repoId, string filePath)` | Returns all symbols and dependencies for a single file. Joins `symbols` and `dependencies` on `file_id`. Returns a `ModuleApi` model containing the file metadata, symbol list, and dependency list. `filePath` is validated against the repo root via `PathValidator`. |

#### Dependency Graph

| Method | Signature | Behavior |
|--------|-----------|----------|
| `GetDependencyGraph` | `Task<DependencyGraph> GetDependencyGraphAsync(string repoId, string? rootFile, string direction, int depth)` | Traverses the dependency graph starting from `rootFile` (or all files if null). `direction` is `"dependents"` (who depends on me) or `"dependencies"` (what do I depend on). `depth` limits traversal depth (default 3, max 10). Implementation uses either a recursive CTE or iterative BFS with parameterized queries at each level. Returns a `DependencyGraph` model with nodes and edges. |

#### Change Detection

| Method | Signature | Behavior |
|--------|-----------|----------|
| `GetChangedFiles` | `Task<ChangedFilesResult> GetChangedFilesAsync(string repoId, long snapshotId)` | Loads the snapshot's `file_hashes` JSON, loads current file hashes from `files` table, and diffs them. Returns three lists: `added` (in current but not snapshot), `modified` (hash changed), `removed` (in snapshot but not current). |

### Result Models (src/CodeCompress.Core/Models/)

| Model | Fields |
|-------|--------|
| `SymbolSearchResult` | `Symbol`, `FilePath`, `Rank` |
| `TextSearchResult` | `FilePath`, `Snippet`, `Rank` |
| `ProjectOutline` | `RepoId`, `Groups` (list of `OutlineGroup` with name, symbols, children) |
| `ModuleApi` | `File`, `Symbols`, `Dependencies` |
| `DependencyGraph` | `Nodes` (list of file paths), `Edges` (list of `{From, To, Alias?}`) |
| `ChangedFilesResult` | `Added`, `Modified`, `Removed` (each a list of `FileRecord`) |

## Acceptance Criteria

- [ ] `Fts5Sanitizer.Sanitize` strips all FTS5 special operators and returns safe quoted terms
- [ ] `Fts5Sanitizer.Sanitize` returns empty string for null, empty, or whitespace-only input
- [ ] `Fts5Sanitizer.Sanitize` handles adversarial input (e.g., `"NEAR/3 OR DROP TABLE"`, `"name:*"`, `")(--"`)
- [ ] `SearchSymbolsAsync` returns FTS5-ranked results matching the query
- [ ] `SearchSymbolsAsync` filters by `kind` when provided
- [ ] `SearchSymbolsAsync` respects the `limit` parameter and enforces a server-side maximum
- [ ] `SearchTextAsync` returns file paths and snippets matching the query
- [ ] `SearchTextAsync` filters by glob pattern on `relative_path` when provided
- [ ] `GetSymbolByNameAsync` returns exact match and handles qualified names (e.g., `ModuleName.FunctionName`)
- [ ] `GetSymbolsByNamesAsync` returns correct results for batch lookups and handles missing names gracefully
- [ ] `GetProjectOutlineAsync` groups symbols by file or kind, respects `includePrivate` and `maxDepth`
- [ ] `GetModuleApiAsync` returns all symbols and dependencies for a given file path
- [ ] `GetModuleApiAsync` validates the file path against the repository root
- [ ] `GetDependencyGraphAsync` traverses in both directions (`dependents` and `dependencies`)
- [ ] `GetDependencyGraphAsync` respects the `depth` limit and caps at a maximum of 10
- [ ] `GetChangedFilesAsync` correctly identifies added, modified, and removed files compared to a snapshot
- [ ] All SQL uses `@param` parameterized syntax — zero string concatenation
- [ ] All FTS5 queries pass through `Fts5Sanitizer.Sanitize` before reaching SQLite
- [ ] No `dynamic` or `object` types in public API signatures
- [ ] All tests pass: `SymbolStoreQueryTests`, `Fts5SanitizerTests`

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `src/CodeCompress.Core/Storage/Fts5Sanitizer.cs` | FTS5 query sanitization utility |
| `src/CodeCompress.Core/Models/SymbolSearchResult.cs` | Search result model |
| `src/CodeCompress.Core/Models/TextSearchResult.cs` | Text search result model |
| `src/CodeCompress.Core/Models/ProjectOutline.cs` | Outline aggregation model |
| `src/CodeCompress.Core/Models/ModuleApi.cs` | Module API model |
| `src/CodeCompress.Core/Models/DependencyGraph.cs` | Dependency graph model |
| `src/CodeCompress.Core/Models/ChangedFilesResult.cs` | Change detection result model |
| `tests/CodeCompress.Core.Tests/Storage/SymbolStoreQueryTests.cs` | Query method tests |
| `tests/CodeCompress.Core.Tests/Storage/Fts5SanitizerTests.cs` | Sanitizer unit tests |

### Modified Files

| File | Change |
|------|--------|
| `src/CodeCompress.Core/Storage/ISymbolStore.cs` | Add all new query method signatures |
| `src/CodeCompress.Core/Storage/SymbolStore.cs` | Implement all new query methods |

## Out of Scope

- MCP tool implementations that call these methods (Feature 05+)
- Index engine orchestration and incremental re-indexing (Feature 04+)
- Caching of query results in memory
- Pagination beyond simple `LIMIT` (no cursor-based pagination)
- Cross-repository queries or federated search
- Write operations (already covered in 03-002)

## Notes/Decisions

- **FTS5 sanitization strategy:** The safest approach is to split the user query into individual terms, wrap each in double quotes, and join with spaces (implicit AND in FTS5). This prevents all operator injection while preserving multi-word search. Example: input `"foo OR bar*"` becomes `"foo" "OR" "bar"` — FTS5 treats `"OR"` as a literal term, not an operator.
- **BM25 ranking:** FTS5's built-in `bm25()` function provides relevance ranking. The column weights can be tuned later (e.g., weighting `name` higher than `doc_comment` in `symbols_fts`).
- **Recursive CTE for dependency graph:** SQLite supports `WITH RECURSIVE` which is ideal for graph traversal up to a bounded depth. The depth parameter is enforced in the CTE's recursion termination condition, not in application code, to prevent runaway queries.
- **Glob filtering for text search:** The `glob` parameter on `SearchTextAsync` filters on the `relative_path` column using SQLite's `GLOB` function (case-sensitive, supports `*` and `?`). This is applied as a WHERE clause, not as part of the FTS5 MATCH expression.
- **Snapshot diffing in application code:** `GetChangedFilesAsync` deserializes the snapshot's JSON `file_hashes` and compares in-memory against current file records. This is simpler and more maintainable than a complex SQL join between JSON and the `files` table, and snapshots are bounded in size (thousands of files, not millions).
- **Qualified name lookup:** `GetSymbolByNameAsync` splits on `.` or `:` to separate parent from child symbol names. This supports Luau's `Module.Function` and `Module:Method` conventions as well as C#'s `Namespace.Class.Method` patterns.
