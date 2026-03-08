# 001 — MCP Server Host Setup

## Summary

Configure the MCP server executable using `GenericHost`, the `ModelContextProtocol` SDK, stdio transport, and full DI registration of all core services. `Program.cs` is the composition root that wires every component together — parsers, indexing engine, storage, validation — and starts listening for MCP protocol messages on stdin/stdout. The server auto-discovers tool classes decorated with `[McpServerToolType]`.

## Dependencies

- **Feature 01** — Project scaffold, solution file, `CodeCompress.Server.csproj`.
- **Feature 02** — Core models and interfaces (`ILanguageParser`, `SymbolInfo`, `ParseResult`, etc.).
- **Feature 03** — SQLite storage layer (`SqliteConnectionFactory`, `SymbolStore`).
- **Feature 04** — `PathValidator` for input sanitization.
- **Feature 05** — `LuauParser` (first `ILanguageParser` implementation).
- **Feature 06** — `IndexEngine`, `FileHasher`, `ChangeTracker`.

## Scope

### 1. Program.cs (`src/CodeCompress.Server/Program.cs`)

The composition root builds and runs the generic host:

| Responsibility | Detail |
|---|---|
| Host builder | `Host.CreateDefaultBuilder(args)` with MCP server configuration |
| MCP SDK integration | `.AddMcpServer()` on the service collection with stdio transport; auto-discovers `[McpServerToolType]` classes |
| Structured logging | `ILogger` via `Microsoft.Extensions.Logging`; console logging for diagnostics (stderr only — stdout is the MCP transport) |
| Graceful shutdown | `CancellationToken` propagation; clean SQLite connection disposal |

### 2. DI Registration

All core services registered in a single `IServiceCollection` extension method or directly in `Program.cs`:

| Service | Lifetime | Notes |
|---|---|---|
| `ILanguageParser` implementations | Singleton (collection) | Registered via `AddSingleton<ILanguageParser, LuauParser>()`. Future parsers added the same way. Resolved as `IEnumerable<ILanguageParser>` by `IndexEngine`. |
| `IndexEngine` | Singleton | Stateless orchestrator; holds no per-request state. Receives `IEnumerable<ILanguageParser>`, `SymbolStore`, `FileHasher`, `ChangeTracker`. |
| `SymbolStore` | Singleton | Thread-safe via SQLite WAL mode. Single long-lived connection per database file. |
| `SqliteConnectionFactory` | Singleton | Creates/opens connections to `~/.codecompress/{repo-hash}.db`. |
| `FileHasher` | Singleton | Stateless SHA-256 hasher. |
| `ChangeTracker` | Singleton | Compares file hashes for incremental indexing. |
| `PathValidator` | Singleton | Stateless path canonicalization and traversal checks. |

### 3. Stdio Transport

- MCP protocol messages arrive on `stdin` and responses are written to `stdout`.
- All diagnostic/log output must go to `stderr` to avoid corrupting the MCP message stream.
- The `ModelContextProtocol` SDK handles framing, serialization, and tool dispatch.

### 4. Tool Auto-Discovery

- The SDK scans for classes annotated with `[McpServerToolType]` and methods annotated with `[McpServerTool]`.
- No manual tool registration required — adding a new tool class is a single-file change.
- Tool classes receive dependencies via constructor injection.

### 5. Server Startup Test (`tests/CodeCompress.Server.Tests/ServerHostTests.cs`)

Verify the DI container builds correctly and all critical services resolve without error:

| Test | Description |
|---|---|
| Host_Builds_Without_Errors | Build the host, verify no exceptions during `BuildServiceProvider` |
| AllCoreServices_Resolve | Resolve `IndexEngine`, `SymbolStore`, `PathValidator`, `IEnumerable<ILanguageParser>` — all non-null |
| LanguageParsers_Resolve_AsCollection | `IEnumerable<ILanguageParser>` contains at least `LuauParser` |
| StdioTransport_IsConfigured | Verify MCP server is configured for stdio (not HTTP/SSE) |

## Acceptance Criteria

- [ ] `Program.cs` builds a `GenericHost` with MCP server configuration and stdio transport.
- [ ] All core services (`IndexEngine`, `SymbolStore`, `SqliteConnectionFactory`, `FileHasher`, `ChangeTracker`, `PathValidator`) are registered in DI.
- [ ] `ILanguageParser` implementations are registered as a collection and resolvable as `IEnumerable<ILanguageParser>`.
- [ ] Logging output goes to `stderr` only — `stdout` is reserved for MCP protocol messages.
- [ ] `[McpServerToolType]` classes are auto-discovered by the SDK.
- [ ] Server starts without errors when executed via `dotnet run --project src/CodeCompress.Server`.
- [ ] DI container resolves all services without exceptions (verified by test).
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] All tests pass via `dotnet test tests/CodeCompress.Server.Tests`.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/Program.cs` | Composition root — host builder, DI, MCP server config |
| `tests/CodeCompress.Server.Tests/ServerHostTests.cs` | DI resolution and startup tests |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Server/CodeCompress.Server.csproj` | Add package references (`ModelContextProtocol`, `Microsoft.Extensions.Hosting`) if not already present |

## Out of Scope

- MCP tool implementations — covered in 07-002 and Feature 08.
- HTTP/SSE transport — stdio only for MVP.
- Authentication or authorization — not required for local stdio transport.
- CLI tool (`CodeCompress.Cli`) wiring — separate project with its own composition root.
- Health checks or readiness probes — not applicable for stdio-based server.

## Notes / Decisions

1. **Singleton lifetimes.** All core services are stateless or thread-safe, so singleton lifetime avoids unnecessary allocations. `SymbolStore` relies on SQLite WAL mode for concurrent read safety. Write operations are serialized internally.
2. **Logging to stderr.** This is critical — any `Console.WriteLine` or logger output to stdout will corrupt the MCP JSON-RPC message stream. The host must be configured with a stderr-only logging provider.
3. **Parser registration pattern.** Each `ILanguageParser` is registered individually (`AddSingleton<ILanguageParser, LuauParser>()`). When Phase 2 adds `CSharpParser`, it is a single additional registration line. `IndexEngine` receives all parsers via `IEnumerable<ILanguageParser>` and builds an internal dictionary keyed by file extension.
4. **No lazy initialization.** All services are eagerly validated at startup via the DI container. Fail-fast on misconfiguration.
