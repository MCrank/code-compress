# 003 — QueryTools: search_symbols and search_text (FTS5)

## Summary

Implement full-text search tools powered by SQLite FTS5 virtual tables. `search_symbols` searches the symbol index (names, signatures, doc comments) and `search_text` searches raw file contents. Both tools sanitize FTS5 queries to prevent syntax injection, support advanced search operators (AND, OR, prefix matching), and return ranked results with contextual snippets. These tools enable AI agents to discover relevant code without knowing exact symbol names.

## Dependencies

- **Feature 08-002** — `QueryTools` class with existing tool methods.
- **Feature 03** — `SymbolStore` FTS5 queries (`SearchSymbols`, `SearchText`, `symbols_fts`, `file_content_fts` virtual tables).
- **Feature 04** — `PathValidator` for input sanitization.

## Scope

### 1. search_symbols Tool (added to `src/CodeCompress.Server/Tools/QueryTools.cs`)

```csharp
[McpServerTool(Name = "search_symbols")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `query` | `string` | Yes | — | FTS5 search query |
| `kind` | `string?` | No | `null` | Filter by `SymbolKind` (e.g., `"function"`, `"class"`, `"method"`) |
| `limit` | `int` | No | `20` | Maximum results to return |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Validate `query` is not empty or whitespace.
3. Sanitize `query` via FTS5 query sanitizer (see section 3 below).
4. Validate `kind` against allowed `SymbolKind` values if provided — reject invalid values.
5. Clamp `limit` to range `[1, 100]`.
6. Call `SymbolStore.SearchSymbolsAsync(repoId, sanitizedQuery, kind, limit, cancellationToken)`.
7. Return ranked results:

```json
{
  "query": "<sanitized_query>",
  "total_matches": 7,
  "results": [
    {
      "name": "ProcessAttack",
      "kind": "method",
      "parent": "CombatService",
      "file": "src/services/CombatService.luau",
      "line": 8,
      "signature": "function CombatService:ProcessAttack(attacker: Player, target: Player): DamageResult",
      "snippet": "...processes an attack between two players and returns damage result...",
      "rank": 1
    }
  ]
}
```

8. The `snippet` field contains the FTS5 `snippet()` function output — a context window around the matching terms. This content is from source files and is **untrusted**.

### 2. search_text Tool (added to `src/CodeCompress.Server/Tools/QueryTools.cs`)

```csharp
[McpServerTool(Name = "search_text")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `query` | `string` | Yes | — | FTS5 search query |
| `glob` | `string?` | No | `null` | File pattern filter (e.g., `"*.luau"`, `"src/services/*.lua"`) |
| `limit` | `int` | No | `20` | Maximum results to return |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Validate `query` is not empty or whitespace.
3. Sanitize `query` via FTS5 query sanitizer.
4. Sanitize `glob` if provided — allow only safe glob characters (`*`, `?`, `/`, `.`, alphanumeric). Strip anything else.
5. Clamp `limit` to range `[1, 100]`.
6. Call `SymbolStore.SearchTextAsync(repoId, sanitizedQuery, glob, limit, cancellationToken)`.
7. Return ranked results:

```json
{
  "query": "<sanitized_query>",
  "total_matches": 12,
  "results": [
    {
      "file": "src/services/CombatService.luau",
      "line": 15,
      "snippet": "...local damage = baseDamage * multiplier...",
      "rank": 1
    }
  ]
}
```

### 3. FTS5 Query Sanitization

A dedicated sanitization method that processes the raw query string before passing it to SQLite FTS5:

| Input Pattern | Action |
|---|---|
| Simple words (`damage`, `health`) | Pass through as-is |
| Boolean operators (`AND`, `OR`, `NOT`) | Allow — FTS5 supports these natively |
| Quoted phrases (`"damage result"`) | Allow if balanced quotes; strip unbalanced quotes |
| Prefix matching (`combat*`) | Allow — FTS5 supports prefix queries |
| Column filters (`name:foo`) | **Strip** — prevent targeting specific FTS5 columns |
| Parentheses for grouping | Allow if balanced; strip unbalanced |
| Special FTS5 syntax (`NEAR`, `^`) | **Strip** — prevent advanced operators that could be abused |
| Empty after sanitization | Fall back to plain text search (treat original query as a literal phrase) |

**Fallback behavior:** If the sanitized query causes an FTS5 syntax error at execution time, catch the `SqliteException`, treat the original query as a plain text literal (wrap in double quotes), and retry once.

### 4. Security Considerations

- FTS5 query injection is the primary threat. A crafted query could exploit FTS5 syntax to extract unintended data or cause errors. The sanitizer uses an allow-list approach.
- Column filter stripping prevents targeting internal FTS5 columns (e.g., `rowid:*`).
- `glob` parameter sanitization prevents shell injection patterns — only glob metacharacters are allowed.
- Snippet content originates from source files and is untrusted. Returned as structured JSON fields.
- `limit` is clamped to prevent resource exhaustion from unbounded queries.

## Acceptance Criteria

- [ ] `search_symbols` validates `path`, sanitizes `query`, and returns ranked symbol matches.
- [ ] `search_symbols` supports FTS5 operators: `AND`, `OR`, `NOT`, quoted phrases, prefix matching (`*`).
- [ ] `search_symbols` with `kind` filter returns only symbols of the specified kind.
- [ ] `search_symbols` respects `limit` parameter (clamped to `[1, 100]`).
- [ ] `search_symbols` with malicious FTS5 query sanitizes input without crashing.
- [ ] `search_text` validates `path`, sanitizes `query`, and returns ranked file content matches.
- [ ] `search_text` with `glob` filter returns only matches in files matching the pattern.
- [ ] `search_text` `glob` parameter is sanitized against injection.
- [ ] FTS5 query sanitizer strips column filters, `NEAR`, `^`, and unbalanced quotes/parentheses.
- [ ] Malformed FTS5 queries fall back to plain text search (wrapped in quotes) without error.
- [ ] Empty or whitespace-only `query` returns structured error.
- [ ] Invalid `kind` value returns structured error.
- [ ] All responses return structured data only — no raw user input echoed in freeform fields.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.
- [ ] Tool test coverage is 85%+.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/Sanitization/Fts5QuerySanitizer.cs` | FTS5 query sanitization logic |
| `tests/CodeCompress.Server.Tests/Sanitization/Fts5QuerySanitizerTests.cs` | Sanitizer unit tests |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Server/Tools/QueryTools.cs` | Add `search_symbols` and `search_text` tool methods |
| `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs` | Add unit tests for both search tools |

## Test Cases

### Fts5QuerySanitizerTests (`tests/CodeCompress.Server.Tests/Sanitization/Fts5QuerySanitizerTests.cs`)

| Test | Description |
|---|---|
| Sanitize_SimpleWord_PassesThrough | `"damage"` returns `"damage"` unchanged |
| Sanitize_MultipleWords_PassesThrough | `"damage health"` returns unchanged |
| Sanitize_BooleanOperators_Allowed | `"damage OR health"` returns unchanged |
| Sanitize_QuotedPhrase_Allowed | `"\"damage result\""` returns unchanged |
| Sanitize_PrefixMatch_Allowed | `"combat*"` returns unchanged |
| Sanitize_UnbalancedQuotes_Stripped | `"damage \"result"` strips the unbalanced quote |
| Sanitize_ColumnFilter_Stripped | `"name:foo"` strips the column filter prefix |
| Sanitize_NearOperator_Stripped | `"NEAR(damage, health)"` strips `NEAR` syntax |
| Sanitize_CaretOperator_Stripped | `"^damage"` strips the caret |
| Sanitize_UnbalancedParentheses_Stripped | `"(damage OR"` strips unbalanced parenthesis |
| Sanitize_BalancedParentheses_Allowed | `"(damage OR health)"` returns unchanged |
| Sanitize_EmptyAfterSanitization_FallsBackToLiteral | Input that becomes empty after stripping is wrapped as literal |

### QueryToolsTests — Search Tools (added to `tests/CodeCompress.Server.Tests/Tools/QueryToolsTests.cs`)

| Test | Description |
|---|---|
| SearchSymbols_SimpleQuery_ReturnsRankedResults | `"damage"` returns matching symbols ranked by relevance |
| SearchSymbols_WithOrOperator_ReturnsUnion | `"damage OR health"` returns symbols matching either term |
| SearchSymbols_WithAndOperator_ReturnsIntersection | `"damage AND combat"` returns symbols matching both terms |
| SearchSymbols_PrefixQuery_ReturnsMatches | `"combat*"` matches `CombatService`, `CombatUtils`, etc. |
| SearchSymbols_WithKindFilter_FiltersResults | `kind = "method"` returns only methods |
| SearchSymbols_InvalidKind_ReturnsError | `kind = "invalid"` returns structured error |
| SearchSymbols_WithLimit_RespectsLimit | `limit = 5` returns at most 5 results |
| SearchSymbols_LimitClamped_Above100 | `limit = 500` clamped to 100 |
| SearchSymbols_MaliciousQuery_Sanitized | Column filters and special syntax stripped, no crash |
| SearchSymbols_EmptyQuery_ReturnsError | Empty string returns structured error |
| SearchSymbols_InvalidPath_ReturnsError | Path validation failure returns error |
| SearchSymbols_FallbackOnSyntaxError | Malformed query retries as literal phrase |
| SearchText_SimpleQuery_ReturnsFileMatches | `"multiplier"` returns file locations with snippets |
| SearchText_WithGlobFilter_FiltersFiles | `glob = "*.luau"` returns only `.luau` file matches |
| SearchText_MaliciousGlob_Sanitized | Glob with injection patterns is sanitized |
| SearchText_WithLimit_RespectsLimit | `limit = 10` returns at most 10 results |
| SearchText_EmptyQuery_ReturnsError | Empty string returns structured error |
| SearchText_InvalidPath_ReturnsError | Path validation failure returns error |

All tests use **NSubstitute** to mock `SymbolStore` and `PathValidator`.

## Out of Scope

- FTS5 highlighting (bold/italic markup in snippets) — snippets use plain text with `...` ellipsis.
- Fuzzy/typo-tolerant search — FTS5 does not support this natively.
- Search result pagination (offset-based) — agents can adjust `limit` for now.
- Search across multiple repositories simultaneously.
- Custom FTS5 tokenizer configuration — use SQLite default tokenizer.
- Proximity search (`NEAR`) — stripped by the sanitizer as a security measure.

## Notes / Decisions

1. **FTS5 sanitization as a dedicated class.** The sanitizer is extracted into `Fts5QuerySanitizer` rather than inlined in the tool methods because it is a security-critical component that deserves focused unit testing. It may also be reused by future tools.
2. **Allow-list over deny-list.** The sanitizer permits specific FTS5 syntax (`AND`, `OR`, `NOT`, `*`, balanced quotes/parentheses) and strips everything else. This is more robust than trying to deny-list every possible FTS5 injection vector.
3. **Graceful fallback.** Rather than returning an error when FTS5 rejects a query, the tool retries with the original input wrapped in double quotes (literal phrase match). This provides a degraded but functional experience for queries that contain accidental FTS5 syntax.
4. **Snippet generation.** FTS5's built-in `snippet()` function generates context windows around matching terms. This is more efficient than post-processing in C# because it happens within the SQLite query engine.
5. **Glob sanitization.** The `glob` parameter is used in a SQL `LIKE` or `GLOB` clause, not passed to the file system. Only glob metacharacters (`*`, `?`) and path characters (`/`, `.`, alphanumeric) are allowed. This prevents SQL injection through the glob parameter.
6. **Limit clamping.** The `[1, 100]` range prevents both empty results (limit = 0) and resource exhaustion (limit = 10000). The default of 20 balances token cost with result completeness.
