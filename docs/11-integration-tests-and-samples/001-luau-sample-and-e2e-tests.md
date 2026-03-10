# 001 — Luau Sample Project and End-to-End Integration Tests

## Summary

Create a realistic Luau sample project representing a small Roblox game codebase, then write end-to-end integration tests that exercise the full pipeline — index, query, snapshot, re-index, delta, dependency graph, and file tree — using real components (real SQLite, real parsers, real IndexEngine) with no mocks.

## Dependencies

- **Feature 01** — Project scaffold, solution file.
- **Feature 02** — Core models and interfaces.
- **Feature 03** — SQLite storage layer (`SymbolStore`, `SqliteConnectionFactory`).
- **Feature 04** — `PathValidator`.
- **Feature 05** — `LuauParser`.
- **Feature 06** — `IndexEngine`, `FileHasher`, `ChangeTracker`.
- **Feature 07** — MCP server host and indexing tools (`index_project`, `snapshot_create`, `invalidate_cache`).
- **Feature 08** — Query tools (`project_outline`, `get_symbol`, `get_symbols`, `get_module_api`, `search_symbols`, `search_text`).
- **Feature 09** — Delta tools (`changes_since`, `file_tree`).
- **Feature 10** — Dependency tools (`dependency_graph`).

## Scope

### 1. Luau Sample Project (`samples/luau-sample-project/`)

A small but realistic Roblox project structure covering all Luau parser patterns:

| File | Contents | Patterns Covered |
|---|---|---|
| `src/server/Services/CombatService.luau` | Combat system service class with methods, type annotations, require dependencies | Functions, methods, local functions, doc comments, require() |
| `src/server/Services/AIService.luau` | AI behavior service with pathfinding and decision methods | Functions, generic types, require() |
| `src/server/Services/InventoryService.luau` | Inventory management with CRUD operations | Methods, constants, require() |
| `src/shared/Types/GameTypes.luau` | Shared type definitions for the game | Export types, local types, generic types |
| `src/shared/Types/WeaponTypes.luau` | Weapon-specific type definitions | Export types, type unions/intersections |
| `src/shared/Constants/Config.luau` | Game configuration constants | Constants, module table |
| `src/server/init.server.luau` | Server entry point wiring services together | Top-level require() calls |
| `src/client/init.client.luau` | Client entry point | Require() calls, local functions |

Target: ~5-8 files, ~50-100 symbols total, covering functions, methods, local functions, export types, local types, constants, require() dependencies, doc comments, and generic types.

### 2. End-to-End Integration Tests (`tests/CodeCompress.Integration.Tests/EndToEndTests.cs`)

All tests use real components — real SQLite (temp file per test), real parsers, real IndexEngine. No mocks. Each test method is independent and creates its own database.

| Test | Description |
|---|---|
| IndexProject_LuauSample_CorrectFilesAndSymbolCounts | Index the sample project, verify expected file count and total symbol count |
| ProjectOutline_ReturnsGroupedSymbols | Call `project_outline`, verify symbols are grouped by file with correct kinds and signatures |
| GetSymbol_SpecificFunction_ReturnsSourceCode | Call `get_symbol` for a known function, verify returned source code matches the sample file |
| GetSymbols_BatchRetrieve_AllCorrect | Call `get_symbols` with multiple symbol names, verify all are returned correctly |
| GetModuleApi_CombatService_ReturnsPublicApi | Call `get_module_api` for CombatService, verify complete public API surface |
| SearchSymbols_ByName_ReturnsRankedResults | Search for a partial symbol name, verify results are ranked by relevance |
| SearchText_FindsContentInFiles | Search for a text string, verify matching files and line numbers |
| SnapshotCreate_StoresCurrentState | Create a snapshot, verify it is stored and retrievable |
| ChangesSince_AfterModification_ReportsAccurateDelta | Create snapshot, simulate changes (copy modified file), re-index, call `changes_since`, verify delta report |
| DependencyGraph_RequireRelationships_Correct | Call `dependency_graph`, verify require() relationships match sample project structure |
| DependencyGraph_SingleFile_CorrectEdges | Call `dependency_graph` with root_file, verify edges for that file |
| FileTree_MatchesSampleStructure | Call `file_tree`, verify directory structure matches sample project |
| FileTree_RespectsMaxDepth | Call `file_tree` with max_depth=1, verify truncation |
| InvalidateCache_ForcesFullReindex | Call `invalidate_cache`, re-index, verify all files re-processed |
| FullRoundTrip_IndexQueryModifyReindexDelta | End-to-end: index, query, snapshot, modify, re-index, delta — verify entire flow |

### 3. Test Infrastructure

| Component | Detail |
|---|---|
| Database | Temp SQLite file per test, deleted in `[After(Test)]` cleanup |
| File system | Sample project is read-only; modified files for delta tests are copied to a temp directory |
| DI | Tests build a minimal service collection with real implementations (no host needed) |
| Assertions | TUnit async/fluent: `await Assert.That(...)` |

## Acceptance Criteria

- [ ] Luau sample project exists at `samples/luau-sample-project/` with 5-8 `.luau` files covering all parser patterns.
- [ ] Sample project contains ~50-100 symbols across all files.
- [ ] All integration tests use real components — no mocks.
- [ ] `IndexProject` test verifies correct file and symbol counts.
- [ ] `ProjectOutline` test verifies grouped symbols with correct metadata.
- [ ] `GetSymbol` test verifies source code retrieval matches actual file content.
- [ ] `SearchSymbols` and `SearchText` tests verify FTS5 search functionality.
- [ ] Snapshot and `changes_since` tests verify the full delta workflow.
- [ ] `DependencyGraph` tests verify require() relationship extraction.
- [ ] `FileTree` test verifies annotated directory structure.
- [ ] Full round-trip test exercises index, query, modify, re-index, delta in sequence.
- [ ] Each test is independent — no shared state between tests.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Integration.Tests`.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `samples/luau-sample-project/src/server/Services/CombatService.luau` | Combat service with methods, types, requires |
| `samples/luau-sample-project/src/server/Services/AIService.luau` | AI service with pathfinding methods |
| `samples/luau-sample-project/src/server/Services/InventoryService.luau` | Inventory service with CRUD methods |
| `samples/luau-sample-project/src/shared/Types/GameTypes.luau` | Shared game type definitions |
| `samples/luau-sample-project/src/shared/Types/WeaponTypes.luau` | Weapon type definitions |
| `samples/luau-sample-project/src/shared/Constants/Config.luau` | Game configuration constants |
| `samples/luau-sample-project/src/server/init.server.luau` | Server entry point |
| `samples/luau-sample-project/src/client/init.client.luau` | Client entry point |
| `tests/CodeCompress.Integration.Tests/EndToEndTests.cs` | Full end-to-end integration test class |

### Modify

| File | Description |
|---|---|
| `tests/CodeCompress.Integration.Tests/CodeCompress.Integration.Tests.csproj` | Add project references to Core and Server if not already present |

## Out of Scope

- MCP protocol-level testing (stdin/stdout message framing) — tests call tool classes directly.
- Performance benchmarks — functional correctness only.
- Sample projects for other languages (C# sample is Feature 12-003).
- Negative security tests (path traversal, injection) — those are unit tests in the respective feature plans.

## Notes / Decisions

1. **Real components, no mocks.** Integration tests are meant to verify that all components work together correctly. Mocking would defeat the purpose. Each test creates its own SQLite database in a temp directory to ensure isolation.
2. **Sample project as test fixture.** The sample project files are committed to the repository and treated as read-only test fixtures. Delta tests that simulate modifications copy files to a temp directory and modify the copies.
3. **Test independence.** Each test creates a fresh database and service instances. Tests can run in parallel without conflicts because they have separate temp directories.
4. **Symbol count validation.** Exact symbol counts in assertions should match what the Luau parser extracts. If the parser is updated, these counts may need adjustment — this is intentional, as it catches unintended parser behavior changes.
