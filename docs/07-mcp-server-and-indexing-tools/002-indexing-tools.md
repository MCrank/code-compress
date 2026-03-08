# 002 — IndexingTools: index_project, snapshot_create, invalidate_cache

## Summary

Implement the first MCP tool class — `IndexingTools` — containing three tools that build and manage the code index. These tools are the entry point for AI agents to trigger indexing of a codebase, create named snapshots for delta queries, and force re-indexing when needed. All tools enforce path validation and return structured data only, never echoing raw user input.

## Dependencies

- **Feature 07-001** — MCP server host setup, DI registration, stdio transport.
- **Feature 03** — `SymbolStore` for snapshot creation and cache invalidation.
- **Feature 04** — `PathValidator` for input sanitization.
- **Feature 06** — `IndexEngine` for project indexing orchestration.

## Scope

### 1. IndexingTools Class (`src/CodeCompress.Server/Tools/IndexingTools.cs`)

Decorated with `[McpServerToolType]`. Receives `IndexEngine`, `SymbolStore`, and `PathValidator` via constructor injection.

### 2. index_project Tool

```csharp
[McpServerTool(Name = "index_project")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `language` | `string?` | No | `null` | Filter to a specific language (e.g., `"luau"`) |
| `include_patterns` | `string[]?` | No | `null` | Glob patterns for files to include |
| `exclude_patterns` | `string[]?` | No | `null` | Glob patterns for files to exclude |

**Behavior:**
1. Validate `path` via `PathValidator` — reject traversal attempts, non-existent directories.
2. Call `IndexEngine.IndexProjectAsync(path, language, includePatterns, excludePatterns, cancellationToken)`.
3. Return structured result:

```json
{
  "repo_id": "abc123",
  "files_indexed": 42,
  "files_skipped": 3,
  "symbols_found": 187,
  "duration_ms": 1250
}
```

4. On validation failure, return structured error: `{ "error": "Path validation failed", "code": "INVALID_PATH" }`.
5. Never echo the raw `path` value back in any response field.

### 3. snapshot_create Tool

```csharp
[McpServerTool(Name = "snapshot_create")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |
| `label` | `string` | Yes | — | Human-readable snapshot label |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Sanitize `label`:
   - Strip characters that could be interpreted as agent instructions (markdown directives, system prompt fragments, tool-call-like syntax).
   - Truncate to 128 characters maximum.
   - Allow only alphanumeric characters, spaces, hyphens, underscores, and periods.
3. Call `SymbolStore.CreateSnapshotAsync(repoId, sanitizedLabel, cancellationToken)`.
4. Return structured result:

```json
{
  "snapshot_id": "snap-001",
  "label": "pre-refactor",
  "file_count": 42,
  "symbol_count": 187
}
```

### 4. invalidate_cache Tool

```csharp
[McpServerTool(Name = "invalidate_cache")]
```

| Parameter | Type | Required | Default | Description |
|---|---|---|---|---|
| `path` | `string` | Yes | — | Absolute path to project root |

**Behavior:**
1. Validate `path` via `PathValidator`.
2. Call `SymbolStore.InvalidateCacheAsync(repoId, cancellationToken)` — deletes stored file hashes for the repository, forcing a full re-index on the next `index_project` call.
3. Return structured result:

```json
{
  "success": true,
  "message": "Cache invalidated. Next index operation will perform a full reparse."
}
```

### 5. Security Considerations

- All three tools validate `path` as the first operation — reject before any other processing.
- Structured JSON responses only. No freeform text fields containing user-supplied values.
- `label` sanitization prevents prompt injection via snapshot labels (a malicious agent could pass crafted labels designed to influence future tool output consumption).
- Error messages use fixed strings, not interpolated user input.

## Acceptance Criteria

- [ ] `IndexingTools` is decorated with `[McpServerToolType]` and auto-discovered by the MCP SDK.
- [ ] `index_project` validates `path`, calls `IndexEngine`, and returns structured indexing results.
- [ ] `index_project` rejects paths with traversal attempts (`..`) and paths outside the project root.
- [ ] `index_project` supports optional `language`, `include_patterns`, and `exclude_patterns` parameters.
- [ ] `snapshot_create` validates `path`, sanitizes `label`, creates a snapshot, and returns structured results.
- [ ] `snapshot_create` strips malicious content from `label` (markdown directives, prompt fragments).
- [ ] `snapshot_create` truncates `label` to 128 characters.
- [ ] `invalidate_cache` validates `path`, deletes stored hashes, and returns success confirmation.
- [ ] All tools return structured error responses on validation failure — no exceptions leak to the MCP transport.
- [ ] No tool echoes raw user-supplied input (paths, labels) in response fields.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.
- [ ] Tool test coverage is 85%+.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/Tools/IndexingTools.cs` | MCP tool class with `index_project`, `snapshot_create`, `invalidate_cache` |
| `tests/CodeCompress.Server.Tests/Tools/IndexingToolsTests.cs` | Unit tests for all three tools |

### Modify

None — `[McpServerToolType]` auto-discovery handles registration.

## Test Cases (`tests/CodeCompress.Server.Tests/Tools/IndexingToolsTests.cs`)

| Test | Description |
|---|---|
| IndexProject_ValidPath_ReturnsIndexingResults | Valid directory path triggers indexing, returns file/symbol counts |
| IndexProject_TraversalPath_ReturnsError | Path containing `..` is rejected with structured error |
| IndexProject_NonExistentPath_ReturnsError | Path to non-existent directory returns error |
| IndexProject_WithLanguageFilter_PassesToEngine | `language` parameter forwarded to `IndexEngine` |
| IndexProject_WithGlobPatterns_PassesToEngine | Include/exclude patterns forwarded to `IndexEngine` |
| SnapshotCreate_ValidInputs_ReturnsSnapshotInfo | Valid path and label create snapshot, return structured result |
| SnapshotCreate_MaliciousLabel_IsSanitized | Label containing markdown/prompt injection is stripped to safe characters |
| SnapshotCreate_LongLabel_IsTruncated | Label exceeding 128 characters is truncated |
| SnapshotCreate_InvalidPath_ReturnsError | Invalid path rejected before snapshot creation |
| InvalidateCache_ValidPath_ReturnsSuccess | Cache invalidated, success response returned |
| InvalidateCache_InvalidPath_ReturnsError | Invalid path rejected |
| InvalidateCache_ForcesFullReindex | After invalidation, next `index_project` re-parses all files (integration-style with mocks) |

All tests use **NSubstitute** to mock `IndexEngine`, `SymbolStore`, and `PathValidator`.

## Out of Scope

- Query tools (`project_outline`, `get_symbol`, etc.) — covered in Feature 08.
- Delta tools (`changes_since`, `file_tree`) — separate feature.
- Dependency tools (`dependency_graph`) — separate feature.
- HTTP/SSE transport — stdio only.
- Actual file system interaction in tests — all mocked.

## Notes / Decisions

1. **Structured responses over freeform text.** MCP tool outputs are consumed by AI agents. Returning structured JSON prevents misinterpretation and reduces prompt injection surface. The `ModelContextProtocol` SDK serializes return objects to JSON automatically.
2. **Label sanitization regex.** The allow-list approach (`[a-zA-Z0-9 _.\-]`) is safer than a deny-list. Any character outside this set is stripped. This is intentionally restrictive — snapshot labels are identifiers, not prose.
3. **Error response pattern.** All tools catch `PathValidationException` (or equivalent) and return a structured error object rather than throwing. The MCP SDK should not see unhandled exceptions from tool methods.
4. **NSubstitute for isolation.** Tool tests mock all dependencies to test only the tool logic (validation, parameter forwarding, response shaping). Integration tests that exercise the full stack are in `CodeCompress.Integration.Tests`.
