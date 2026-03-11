# Plan: FTS5 Glob Matching and File Path Filter for Search Tools

**Issue:** [GH-22](https://github.com/MCrank/code-compress/issues/22)
**Branch:** `feature/22-fts5-glob-matching-path-filter`

## Problem Summary

Two search limitations identified during real-world testing:

1. **Glob wildcards destroyed by sanitizer**: `AddMaestro*` returns 0 results because `Fts5Sanitizer` (Core) strips `*` and wraps terms in quotes → `"AddMaestro"` (exact match only). FTS5 natively supports prefix via `term*`, but the sanitizer kills it.
2. **No file path filter on search tools**: `search_symbols` and `search_text` cannot scope results to a directory (e.g., `src/Maestro.Orchestrator/`).

## Architecture Context

There are **two sanitizers** with different roles:

| Layer | Class | File | Purpose |
|-------|-------|------|---------|
| Server | `Fts5QuerySanitizer` | `Server/Sanitization/Fts5QuerySanitizer.cs` | Smart — preserves FTS5 operators (AND, OR, NOT, prefix `*`, quotes, parens) |
| Core | `Fts5Sanitizer` | `Core/Storage/Fts5Sanitizer.cs` | Conservative — strips ALL special chars, quotes every term |

**The bug**: `QueryTools.SearchSymbols` calls the Server sanitizer (`Fts5QuerySanitizer.Sanitize`) which correctly preserves `*` — but then `SqliteSymbolStore.SearchSymbolsAsync` calls the Core sanitizer (`Fts5Sanitizer.Sanitize`) which strips `*` again. **Double sanitization** destroys the prefix query.

### Search Data Flow (Current)
```
Agent query: "AddMaestro*"
  → QueryTools.SearchSymbols()
    → Fts5QuerySanitizer.Sanitize("AddMaestro*") → "AddMaestro*"  ✅ (Server preserves *)
    → scope.Store.SearchSymbolsAsync(repoId, "AddMaestro*", ...)
      → Fts5Sanitizer.Sanitize("AddMaestro*") → "\"AddMaestro\""  ❌ (Core strips * and quotes)
      → FTS5 MATCH "\"AddMaestro\"" → exact match only → 0 results
```

### Glob Pattern Types to Support

| Pattern | Example | FTS5 Native? | Strategy |
|---------|---------|-------------|----------|
| Prefix | `AddMaestro*` | Yes (`term*`) | Pass through to FTS5 |
| Suffix | `*Handler` | No | SQL `s.name LIKE '%Handler'` |
| Contains | `*Maestro*` | No | SQL `s.name LIKE '%Maestro%'` |
| Complex | `I*Service` | No | SQL `s.name LIKE 'I%Service'` |
| No glob | `OrderService` | Yes | Existing FTS5 behavior |
| Wildcard-only | `*` | N/A | Reject as too broad |

---

## Implementation Steps

### Step 1: Create `GlobPattern` value type in Core

**File:** `src/CodeCompress.Core/Storage/GlobPattern.cs` (new)
**Tests:** `tests/CodeCompress.Core.Tests/Storage/GlobPatternTests.cs` (new)

Create a value type that parses a raw query and classifies it as one of the glob pattern types. This encapsulates detection logic and converts globs to SQL LIKE patterns.

```csharp
namespace CodeCompress.Core.Storage;

public enum GlobMatchStrategy
{
    Fts5,       // No wildcards — use FTS5 MATCH as-is
    Prefix,     // "AddMaestro*" — use FTS5 prefix query: AddMaestro*
    SqlLike,    // Suffix/contains/complex — use SQL LIKE on s.name
}

public sealed class GlobPattern
{
    public GlobMatchStrategy Strategy { get; }
    public string Fts5Query { get; }     // For FTS5 MATCH (may be empty for pure SqlLike)
    public string? SqlLikePattern { get; } // For SQL LIKE on s.name (null if pure FTS5)

    public static GlobPattern Parse(string query);
    public static bool IsWildcardOnly(string query); // Returns true for "*", "**", etc.
}
```

**Logic in `Parse`:**
1. If query is only `*` chars → throw / return special "too broad" indicator
2. If query contains no `*` → `Strategy = Fts5`, `Fts5Query = sanitized query`, `SqlLikePattern = null`
3. If query ends with `*` but doesn't start with `*` and has no internal `*` → `Strategy = Prefix`, `Fts5Query = "term*"` (FTS5 native prefix)
4. Otherwise → `Strategy = SqlLike`, convert `*` to `%` for SQL LIKE, `Fts5Query` may be empty or contain extractable terms for FTS5 pre-filtering

**SQL LIKE pattern conversion:**
- `*Handler` → `%Handler`
- `*Maestro*` → `%Maestro%`
- `I*Service` → `I%Service`
- Escape `%` and `_` literals in the non-wildcard parts

**Test cases (TDD first):**
- `"OrderService"` → Fts5 strategy, no LIKE pattern
- `"AddMaestro*"` → Prefix strategy, Fts5Query = `"AddMaestro"*`
- `"*Handler"` → SqlLike strategy, LIKE = `%Handler`
- `"*Maestro*"` → SqlLike strategy, LIKE = `%Maestro%`
- `"I*Service"` → SqlLike strategy, LIKE = `I%Service`
- `"*"` → IsWildcardOnly = true
- `"***"` → IsWildcardOnly = true
- `""` / null → empty/invalid

---

### Step 2: Remove double sanitization — Core `Fts5Sanitizer` no longer called in search path

**File:** `src/CodeCompress.Core/Storage/SqliteSymbolStore.cs`
**Tests:** existing integration tests + new unit tests

The Core `Fts5Sanitizer.Sanitize()` call inside `SearchSymbolsAsync` and `SearchTextAsync` must be removed. The Server layer (`Fts5QuerySanitizer`) is the single sanitization point — by the time the query reaches the store, it's already sanitized.

**Changes:**
- `SearchSymbolsAsync`: Remove `var sanitized = Fts5Sanitizer.Sanitize(query);` — use `query` directly
- `SearchTextAsync`: Remove `var sanitized = Fts5Sanitizer.Sanitize(query);` — use `query` directly
- Keep the empty-check guard but check the incoming `query` parameter directly

**Why this is safe:** The Server `Fts5QuerySanitizer` already handles all injection vectors (column filters, NEAR, unbalanced quotes/parens, caret). The Core sanitizer's additional quoting is what destroys valid FTS5 operators.

> **Note:** The Core `Fts5Sanitizer` class should NOT be deleted — it may be used by other code paths (e.g., CLI tool). Just stop calling it in the search methods.

---

### Step 3: Update `Fts5QuerySanitizer` to handle glob patterns

**File:** `src/CodeCompress.Server/Sanitization/Fts5QuerySanitizer.cs`
**Tests:** `tests/CodeCompress.Server.Tests/Sanitization/Fts5QuerySanitizerTests.cs`

Currently, `Fts5QuerySanitizer.Sanitize` passes `*` through — this is correct for FTS5 prefix queries. But we need to handle the case where the query is a glob pattern that requires SQL LIKE instead of FTS5 MATCH.

**New method:**
```csharp
/// <summary>
/// Analyzes a raw search query and returns a sanitized GlobPattern
/// that indicates how to execute the search (FTS5, prefix, or SQL LIKE).
/// </summary>
internal static GlobPattern SanitizeAsGlob(string query);
```

This method:
1. Calls `GlobPattern.IsWildcardOnly(query)` — reject if true
2. Calls `GlobPattern.Parse(query)` to classify
3. For `Fts5` and `Prefix` strategies: runs existing `Sanitize()` on the FTS5 query portion
4. For `SqlLike` strategy: sanitizes the LIKE pattern (no SQL injection via `%`/`_` in user input — only our converted wildcards)

**Test cases:**
- `"AddMaestro*"` → GlobPattern with Prefix strategy, Fts5Query sanitized
- `"*Handler"` → GlobPattern with SqlLike strategy
- `"*"` → rejected (too broad)
- `"Order AND *Handler"` → complex case: fall back to SqlLike with `%Handler` for name, `Order` for FTS5

---

### Step 4: Update `ISymbolStore` and `SqliteSymbolStore` search signatures

**Files:**
- `src/CodeCompress.Core/Storage/ISymbolStore.cs`
- `src/CodeCompress.Core/Storage/SqliteSymbolStore.cs`

**Tests:**
- `tests/CodeCompress.Core.Tests/Storage/SqliteSymbolStoreSearchTests.cs` (new or extend existing)

#### 4a: Add `pathFilter` parameter to search methods

```csharp
// Before:
Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(string repoId, string query, string? kind, int limit);
Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(string repoId, string query, string? glob, int limit);

// After:
Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(string repoId, string query, string? kind, int limit, string? pathFilter = null);
Task<IReadOnlyList<TextSearchResult>> SearchTextAsync(string repoId, string query, string? glob, int limit, string? pathFilter = null);
```

**SQL changes for `SearchSymbolsAsync`:**

When `pathFilter` is provided, add:
```sql
AND f.relative_path LIKE @pathPrefix || '%'
```

When `pathFilter` is provided for `SearchTextAsync`, add:
```sql
AND fts.relative_path LIKE @pathPrefix || '%'
```

The `pathFilter` value is already validated/sanitized by `PathValidator.ValidatePathFilter` before reaching the store, and is passed as a parameterized value — safe from injection.

#### 4b: Add glob/LIKE search path to `SearchSymbolsAsync`

Add an overload or modify `SearchSymbolsAsync` to accept a `GlobPattern` instead of a raw query string, or accept additional optional `string? nameLikePattern` parameter:

```csharp
Task<IReadOnlyList<SymbolSearchResult>> SearchSymbolsAsync(
    string repoId, string query, string? kind, int limit,
    string? pathFilter = null, string? nameLikePattern = null);
```

When `nameLikePattern` is provided (suffix/contains/complex glob):
- If `query` is also non-empty: use FTS5 MATCH for pre-filtering + `s.name LIKE @namePattern` for precision
- If `query` is empty (pure LIKE search): skip FTS5 MATCH entirely, query `symbols` + `files` tables directly with `s.name LIKE @namePattern`

**SQL for LIKE-only path (no FTS5):**
```sql
SELECT s.id, s.file_id, s.name, s.kind, s.signature, s.parent_symbol,
       s.byte_offset, s.byte_length, s.line_start, s.line_end, s.visibility, s.doc_comment,
       f.relative_path, 0.0 AS rank
FROM symbols s
JOIN files f ON f.id = s.file_id
WHERE f.repo_id = @repoId AND s.name LIKE @namePattern
```

Plus optional `AND s.kind = @kind` and `AND f.relative_path LIKE @pathPrefix || '%'`.

**Security:** The `nameLikePattern` is generated by `GlobPattern.Parse` from sanitized input — not raw user input. The `%` wildcards are placed programmatically. Any `%` or `_` in the original query terms are escaped.

---

### Step 5: Update `QueryTools` to use glob-aware search

**File:** `src/CodeCompress.Server/Tools/QueryTools.cs`
**Tests:** `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs` (extend)

#### 5a: `SearchSymbols` — add `pathFilter` and glob detection

```csharp
[McpServerTool(Name = "search_symbols")]
[Description("Search the symbol index using FTS5 full-text search. Supports prefix*, *suffix, *contains*, and I*Pattern glob matching. Optionally filter by file path.")]
public async Task<string> SearchSymbols(
    [Description("...")] string path,
    [Description("Search query — supports plain text, FTS5 operators (AND, OR, NOT), and glob patterns (prefix*, *suffix, *contains*)")] string query,
    [Description("Filter by symbol kind (...)")] string? kind = null,
    [Description("Filter results to files under this relative directory path (e.g., 'src/Core/Models')")] string? pathFilter = null,
    [Description("Maximum results to return (1-100)")] int limit = 20,
    CancellationToken cancellationToken = default)
```

**Implementation changes:**
1. Validate `pathFilter` with `PathValidator.ValidatePathFilter` (same pattern as `ProjectOutline`)
2. Detect if query is a wildcard-only pattern → return error "query is too broad"
3. Call `Fts5QuerySanitizer.SanitizeAsGlob(query)` to get a `GlobPattern`
4. Based on strategy:
   - `Fts5` / `Prefix` → call `SearchSymbolsAsync(repoId, globPattern.Fts5Query, kind, limit, pathFilter)`
   - `SqlLike` → call `SearchSymbolsAsync(repoId, globPattern.Fts5Query, kind, limit, pathFilter, globPattern.SqlLikePattern)`
5. Keep the `DbException` catch for FTS5 syntax error fallback

#### 5b: `SearchText` — add `pathFilter`

```csharp
[McpServerTool(Name = "search_text")]
[Description("Search raw indexed file contents using FTS5 full-text search. Use for string literals, comments, or non-symbol patterns. Supports glob and path filtering.")]
public async Task<string> SearchText(
    [Description("...")] string path,
    [Description("...")] string query,
    [Description("File pattern filter (e.g., *.luau, src/services/*.lua)")] string? glob = null,
    [Description("Filter results to files under this relative directory path (e.g., 'src/Config')")] string? pathFilter = null,
    [Description("Maximum results to return (1-100)")] int limit = 20,
    CancellationToken cancellationToken = default)
```

**Implementation changes:**
1. Validate `pathFilter` with `PathValidator.ValidatePathFilter`
2. Pass `pathFilter` through to `SearchTextAsync`
3. Text search doesn't need glob-on-name logic (it searches file content, not symbol names), so only the `pathFilter` addition applies here

---

### Step 6: Integration tests

**File:** `tests/CodeCompress.Integration.Tests/SearchIntegrationTests.cs` (new)

End-to-end tests that:
1. Index a small test project with known symbols
2. Run `search_symbols` with various glob patterns and verify results
3. Run `search_symbols` with `pathFilter` and verify scoping
4. Run `search_text` with `pathFilter` and verify scoping
5. Verify wildcard-only `*` returns error
6. Verify `pathFilter` rejects traversal (`../../etc/`)
7. Verify backward compatibility (no globs = existing behavior)

---

## Step Execution Order & Dependencies

```
Step 1 (GlobPattern) ─────────────────┐
                                       ├──→ Step 3 (Sanitizer update)
Step 2 (Remove double sanitization) ──┤                │
                                       │                ▼
                                       ├──→ Step 4 (Store signatures + SQL)
                                       │                │
                                       │                ▼
                                       └──→ Step 5 (QueryTools update)
                                                        │
                                                        ▼
                                              Step 6 (Integration tests)
```

Steps 1 and 2 can be done in parallel. Steps 3-4 depend on Step 1. Step 5 depends on 3+4. Step 6 is last.

---

## Files Changed Summary

| File | Action | Description |
|------|--------|-------------|
| `src/CodeCompress.Core/Storage/GlobPattern.cs` | **New** | Glob pattern parser and classifier |
| `src/CodeCompress.Core/Storage/Fts5Sanitizer.cs` | Unchanged | Keep as-is (used by other paths) |
| `src/CodeCompress.Core/Storage/ISymbolStore.cs` | Modify | Add `pathFilter` + `nameLikePattern` params to search methods |
| `src/CodeCompress.Core/Storage/SqliteSymbolStore.cs` | Modify | Remove double sanitization, add LIKE query path, add pathFilter SQL |
| `src/CodeCompress.Server/Sanitization/Fts5QuerySanitizer.cs` | Modify | Add `SanitizeAsGlob` method |
| `src/CodeCompress.Server/Tools/QueryTools.cs` | Modify | Add `pathFilter` param, glob-aware dispatch |
| `tests/CodeCompress.Core.Tests/Storage/GlobPatternTests.cs` | **New** | Unit tests for GlobPattern |
| `tests/CodeCompress.Core.Tests/Storage/SqliteSymbolStoreSearchTests.cs` | **New** | Unit tests for updated search SQL |
| `tests/CodeCompress.Server.Tests/Sanitization/Fts5QuerySanitizerTests.cs` | Modify | Add glob-aware sanitization tests |
| `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs` | Modify | Add pathFilter + glob tests |
| `tests/CodeCompress.Integration.Tests/SearchIntegrationTests.cs` | **New** | End-to-end search tests |

## Security Checklist

- [ ] `GlobPattern.Parse` only produces `%` wildcards from `*` — no raw user `%` or `_` passed through
- [ ] `pathFilter` validated via `PathValidator.ValidatePathFilter` (rejects `..`, absolute paths, SQL wildcards)
- [ ] All SQL remains parameterized — zero string concatenation
- [ ] Wildcard-only queries (`*`) rejected with error
- [ ] FTS5 injection vectors still handled by `Fts5QuerySanitizer`
- [ ] LIKE patterns use `ESCAPE` clause if needed for literal `%`/`_` in search terms

## Backward Compatibility

- Queries with no `*` behave exactly as before (FTS5 MATCH)
- `pathFilter` defaults to `null` — omitting it returns all results
- `nameLikePattern` defaults to `null` — no LIKE filtering unless glob detected
- Existing `search_text` `glob` parameter unchanged
