# 001 — DependencyTools: dependency_graph

## Summary

Graph traversal tool that exposes import/require relationships between files in an indexed project. Supports directional queries (who I depend on, who depends on me, or both), depth-limited traversal, and full project graph generation. Returns an adjacency list with metadata in a token-efficient structured format.

## Dependencies

- **Feature 07** — MCP server host, DI, `[McpServerToolType]` infrastructure.
- **Feature 03** — `SymbolStore` dependency storage and query methods (`GetDependencyGraph`, `GetFileDependencies`, `GetFileDependents`).
- **Feature 04** — `PathValidator` for input sanitization.
- **Feature 05** — `LuauParser` captures `require()` statements as `DependencyInfo`.
- **Feature 06** — `IndexEngine` stores parsed dependencies in `SymbolStore`.

## Scope

### 1. DependencyTools Class (`src/CodeCompress.Server/Tools/DependencyTools.cs`)

A `[McpServerToolType]` class containing one `[McpServerTool]` method. Receives `SymbolStore` and `PathValidator` via constructor injection.

### 2. dependency_graph Tool

| Aspect | Detail |
|---|---|
| Parameters | `path` (string, required) — project root; `root_file` (string?, optional) — start traversal from a specific file; `direction` (string, default "both") — one of "dependencies", "dependents", "both"; `depth` (int?, default unlimited) — maximum traversal depth |
| Path validation | `PathValidator.Validate(path)` and `PathValidator.Validate(root_file)` if provided — reject traversal attempts |
| Direction semantics | `"dependencies"` = outgoing edges (files I require/import); `"dependents"` = incoming edges (files that require/import me); `"both"` = both directions |
| Traversal | BFS from `root_file` with depth limit. Each level expands edges in the specified direction. Visited set prevents revisiting nodes (handles circular dependencies). |
| Full project mode | If `root_file` is omitted, return all files and their relationships (no traversal, just the full adjacency list from `SymbolStore`) |
| Depth clamping | `depth` is clamped to range 1-50; if omitted or null, no depth limit (full reachability) |

### 3. Output Format

Structured adjacency list, one entry per file, with directional edges:

```
Dependency graph for "CombatService.luau" (depth: 2, direction: both):

CombatService.luau
  requires -> GameTypes.luau, WeaponConfig.luau, Config.luau
  required by -> AgentService.luau, MissionService.luau

GameTypes.luau
  requires -> (none)
  required by -> CombatService.luau, AIService.luau

WeaponConfig.luau
  requires -> GameTypes.luau
  required by -> CombatService.luau
```

For direction "dependencies" only outgoing edges are shown; for "dependents" only incoming edges. Files with no edges in the requested direction are still listed with `(none)`.

Full project mode includes a summary line: `Total: N files, M dependency edges`.

### 4. Graph Traversal Algorithm

```
BFS(root_file, direction, max_depth):
  queue = [(root_file, 0)]
  visited = {root_file}
  result = {}
  while queue not empty:
    (file, level) = dequeue
    if max_depth != null and level >= max_depth: continue expanding
    edges = get_edges(file, direction)
    result[file] = edges
    for neighbor in edges:
      if neighbor not in visited:
        visited.add(neighbor)
        queue.enqueue((neighbor, level + 1))
  return result
```

### 5. Security

- All path parameters validated via `PathValidator`.
- `direction` parameter validated against allowed values ("dependencies", "dependents", "both") — reject anything else.
- `depth` clamped to prevent excessive traversal.
- Output is structured data only — file paths from the index, no raw user input echoed.

### 6. Tests (`tests/CodeCompress.Server.Tests/Tools/DependencyToolsTests.cs`)

| Test | Description |
|---|---|
| SingleFile_Dependencies_ReturnsOutgoingEdges | File with requires, direction="dependencies" — lists required files |
| SingleFile_Dependents_ReturnsIncomingEdges | File required by others, direction="dependents" — lists dependent files |
| SingleFile_Both_ReturnsBothDirections | direction="both" — lists both requires and required-by |
| DepthLimiting_StopsAtSpecifiedDepth | depth=1 returns only direct edges, not transitive |
| FullProjectGraph_NoRootFile_ReturnsAllEdges | No root_file — returns complete adjacency list with summary |
| FileWithNoDependencies_ReturnsEmptyEdges | Isolated file returns `(none)` for both directions |
| NonExistentFile_ReturnsError | root_file not in index — clear error message |
| CircularDependencies_NoInfiniteLoop | A requires B, B requires A — traversal terminates correctly |
| InvalidDirection_ReturnsError | direction="invalid" — rejected with allowed values listed |
| PathValidation_RejectsTraversal | Path with `..` or outside project root is rejected |
| DepthClamping_ExcessiveDepth_Clamped | depth=999 clamped to 50 |
| MultipleRootsInGraph_CorrectTraversal | Complex graph with multiple entry points traversed correctly |
| TransitiveDependencies_FullChain | A->B->C->D with unlimited depth — all nodes included |

## Acceptance Criteria

- [ ] `DependencyTools` class is annotated with `[McpServerToolType]` and auto-discovered by the MCP server.
- [ ] `dependency_graph` supports three direction modes: "dependencies", "dependents", "both".
- [ ] Depth-limited BFS traversal correctly stops at the specified depth.
- [ ] Full project graph is returned when `root_file` is omitted.
- [ ] Circular dependencies are handled without infinite loops.
- [ ] Non-existent `root_file` returns a clear error message.
- [ ] Invalid `direction` values are rejected with allowed values listed.
- [ ] All path parameters are validated via `PathValidator`.
- [ ] Output is structured data only — no prompt injection vectors.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.
- [ ] 85%+ code coverage for `DependencyTools`.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/Tools/DependencyTools.cs` | `[McpServerToolType]` class with `dependency_graph` tool |
| `tests/CodeCompress.Server.Tests/Tools/DependencyToolsTests.cs` | Unit tests for dependency graph traversal |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Core/Storage/SymbolStore.cs` | Add `GetFileDependencies`, `GetFileDependents`, `GetFullDependencyGraph` methods if not already present |

## Out of Scope

- Visualizing graphs (DOT/Graphviz output) — text adjacency list only for MVP.
- Cross-project dependencies — only intra-project relationships.
- Dependency version resolution — not applicable for source-level require/import.
- Cycle detection reporting (listing cycles explicitly) — cycles are tolerated, not reported.

## Notes / Decisions

1. **BFS over DFS.** BFS gives a natural depth-limited traversal and returns results ordered by distance from root. This is more useful for AI agents that want to understand the immediate neighborhood of a file before expanding outward.
2. **Circular dependency handling.** The visited set prevents infinite loops. Cycles are not explicitly reported — the graph simply shows edges as they exist. A future enhancement could add cycle detection.
3. **Full project graph performance.** For large projects, the full adjacency list may be large. This is acceptable for MVP — the tool is primarily useful with `root_file` scoping. A future enhancement could add pagination or filtering.
4. **Direction parameter as string.** Using a string enum rather than a dedicated C# enum because MCP tool parameters are JSON strings. Validation happens at the tool level with a clear error message for invalid values.
