---
name: implement-plan
description: Implement a feature from a mini-plan document using TDD, enforcing security and .NET/MCP best practices. Pass the path to a mini-plan .md file.
argument-hint: [mini-plan-path]
disable-model-invocation: true
---

# Implement Mini-Plan

You are implementing a feature for **CodeCompress**, a .NET 10 / C# 14 MCP server. Read and follow the mini-plan document provided, enforcing strict TDD methodology, OWASP security requirements, and .NET/MCP best practices throughout.

## Documentation Lookup Policy (Mandatory)

**Never rely on training data for library APIs, SDK usage, or framework patterns.** Always fetch current documentation before using any library or framework API.

Use the **Context7 MCP** (`resolve-library-id` → `query-docs`) as the primary documentation source:
1. Resolve the library ID first: e.g., `resolve-library-id("ModelContextProtocol")`, `resolve-library-id("TUnit")`, `resolve-library-id("Microsoft.Data.Sqlite")`
2. Then query for the specific API/pattern you need: e.g., `query-docs(id, "McpServerToolType tool registration")`

Use the **Ref MCP** (`ref_search_documentation` / `ref_read_url`) as a secondary source when Context7 lacks coverage or you need to read a specific documentation URL.

**When to look up docs:**
- Before using any NuGet package API for the first time in a session (ModelContextProtocol, TUnit, NSubstitute, Verify, Microsoft.Data.Sqlite, Microsoft.Extensions.Hosting)
- When implementing MCP tool attributes, transport setup, or protocol patterns
- When writing TUnit assertions, test attributes, or test lifecycle hooks
- When writing NSubstitute mock setups or Verify snapshot configurations
- When using SQLite APIs, FTS5 syntax, or PRAGMA configurations
- When unsure about any .NET 10 / C# 14 API or language feature
- When delegating to agents — include the relevant doc snippets in the agent prompt so they don't guess either

**Do NOT guess at API signatures, attribute names, or SDK patterns.** A 30-second doc lookup prevents hours of debugging wrong APIs.

## Step 0: Load Context

1. Read the mini-plan at `$ARGUMENTS`
2. Read `CLAUDE.md` for project conventions
3. Read the **Dependencies** section of the mini-plan — verify all prerequisite features exist (check that the files/types referenced are actually present in the codebase). If dependencies are missing, stop and report what's missing.
4. Read the **Files to Create/Modify** section to understand the full scope.
5. **Look up documentation** for any libraries/frameworks referenced in the mini-plan scope using Context7 or Ref MCPs. Cache key API patterns before starting implementation.

## Step 1: Plan the Implementation Order

Before writing any code, determine the correct implementation order:
- Which types/interfaces need to exist before others?
- Which tests should be written first?
- Are there any shared helpers or base classes needed?

State your implementation order briefly, then proceed.

## Step 2: TDD Cycle (Mandatory)

For **every** deliverable in the mini-plan, follow this strict cycle:

### 2a. Write Failing Tests First
- Create the test class **before** the implementation class
- Use **TUnit** with async/fluent assertions:
  ```csharp
  await Assert.That(result).IsEqualTo(expected);
  ```
- Use `[Arguments(...)]` for parameterized tests
- Use **NSubstitute** for mocking interfaces
- Use **Verify** for snapshot testing complex outputs
- Test class location must mirror source: `CodeCompress.Core/Foo/Bar.cs` → `CodeCompress.Core.Tests/Foo/BarTests.cs`
- Include edge cases, error cases, and boundary conditions from the mini-plan's test section

### 2b. Verify Tests Fail
- Run: `dotnet test --filter "FullyQualifiedName~TestClassName"` to confirm tests fail (red phase)
- If tests pass without implementation, the tests are wrong — fix them

### 2c. Write Minimum Implementation
- Write the **minimum code** to make tests pass — no more
- Follow the signatures, types, and patterns specified in the mini-plan

### 2d. Verify Tests Pass
- Run: `dotnet test --filter "FullyQualifiedName~TestClassName"` to confirm all tests pass (green phase)
- If any test fails, fix the implementation (not the test, unless the test has a bug)

### 2e. Refactor
- Clean up while keeping tests green
- Apply code style from `.editorconfig` (PascalCase public, `_camelCase` private, Allman braces, 4-space indent)
- Ensure `readonly` where possible, `var` when type is apparent

### 2f. Full Build Check
- Run: `dotnet build CodeCompress.slnx` — must produce **zero warnings**
- SonarAnalyzer.CSharp violations are build errors — fix them immediately

## Step 3: Security Enforcement

For **every** file you create or modify, verify these requirements. Delegate to the security expert agent when implementing security-critical components.

- **Path parameters**: All file paths validated via `PathValidator` — `Path.GetFullPath()` + starts-with check against project root. No `..` traversal. Reject paths outside root.
- **SQL**: All queries use `@param` parameterized syntax. **Zero string concatenation** in SQL. Verify this for every query you write.
- **FTS5**: Search queries sanitized via `Fts5QuerySanitizer` before passing to SQLite. No raw user input in FTS5 syntax.
- **Output**: MCP tool responses return only structured data. Never echo raw user-supplied input (paths, queries, labels) into freeform text without sanitization. Treat source file contents as untrusted.
- **Types**: No `dynamic` or `object` for user-facing data. All types are strongly-typed records/enums/classes.
- **Prompt injection**: Strip or escape content that could be interpreted as agent instructions from file contents, symbol names, doc comments, or FTS5 results.

## Step 4: Agent Delegation

Use specialized agents for complex subtasks. Launch agents in parallel when their work is independent.

### When to delegate:

- **Security-critical code** (PathValidator, SQL queries, FTS5 sanitization, output sanitization): Use the security expert agent for review after implementation.
- **Complex .NET patterns** (DI registration, GenericHost setup, async pipelines, Span-based parsing): Use the .NET expert agent for implementation guidance when unsure.
- **MCP SDK integration** (tool registration, [McpServerToolType], stdio transport, protocol compliance): Use the MCP expert agent to verify correct SDK usage. Fetch latest docs from context7 or ref tools.
- **Test infrastructure** (TUnit setup, Verify snapshot configuration, NSubstitute patterns): Delegate test scaffolding to agents when setting up new test projects.
- **Parallel implementation**: When the mini-plan has independent components (e.g., multiple CRUD methods, multiple test classes), launch agents in parallel to implement them.

### Agent instructions pattern:
When delegating, always include:
1. The specific files to create/modify
2. The exact interfaces/types to implement
3. The project conventions from CLAUDE.md
4. The security requirements relevant to that component
5. The TDD requirement — tests first, then implementation
6. **Relevant documentation snippets** — look up the APIs the agent will need via Context7/Ref MCPs and include the key patterns in the agent prompt. Agents cannot call MCP tools, so they depend on you providing accurate API references.

## Step 5: Verification

After all implementation is complete:

1. **Full test suite**: `dotnet test CodeCompress.slnx` — all tests pass
2. **Zero warnings**: `dotnet build CodeCompress.slnx` — clean build
3. **Acceptance criteria**: Go through every checkbox in the mini-plan's **Acceptance Criteria** section and verify each one is met
4. **Coverage check**: Verify test coverage meets targets from CLAUDE.md (Parsers 95%+, Storage 90%+, IndexEngine 90%+, MCP Tools 85%+)
5. **Security audit**: Verify no parameterized query violations, no path traversal gaps, no unsanitized output

## Step 6: Summary

When complete, provide:
- List of files created/modified
- Test count and pass/fail status
- Any deviations from the mini-plan (with justification)
- Any open items or follow-up needed

## Key Reference

- [CLAUDE.md](../../../CLAUDE.md) — Project conventions, architecture, security requirements
- [PRD](../../../docs/code-compress-prd.md) — Full product requirements (for context, not implementation)
