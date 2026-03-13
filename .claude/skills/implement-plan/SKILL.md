---
name: implement-plan
description: Implement a feature from a mini-plan document, user story, or GitHub issue using TDD, enforcing security and .NET/MCP best practices. Pass the path to a mini-plan .md file, user story, or GitHub issue URL/file. Also use when the user says "implement issue #N" or provides a story/issue to build against.
argument-hint: [mini-plan-path | user-story-path | github-issue-url]
disable-model-invocation: true
---

# Implement Feature

You are implementing a feature for **CodeCompress**, a .NET 10 / C# 14 MCP server. Read and follow the input document provided — this may be a **mini-plan**, a **user story**, or a **GitHub issue**. Enforce strict TDD methodology, OWASP security requirements, and .NET/MCP best practices throughout.

## Input Detection

Determine the input type from `$ARGUMENTS`:

- **Mini-plan** (`.md` file with "Files to Create/Modify" and "Acceptance Criteria" sections): Follow the plan as-is — it already contains implementation details, file lists, and test specifications.
- **User story / GitHub issue** (a `.md` file, plain text, or URL describing desired behavior in terms of user-facing outcomes, acceptance criteria, or "As a … I want … so that …" format): You must **derive the implementation plan yourself** before coding. See [Deriving a Plan from a Story/Issue](#deriving-a-plan-from-a-storyissue) below.

If `$ARGUMENTS` is a GitHub issue URL, fetch its content using available tools (e.g., `gh issue view` CLI if available, or Ref MCP `ref_read_url`) before proceeding.

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

## Step 0: Codebase Discovery via CodeCompress MCP (Mandatory)

Before reading any plan or writing any code, bootstrap your understanding of the codebase using the **CodeCompress MCP server**. This is mandatory — do not skip this step or substitute manual file browsing.

### 0a. Force Index Update

Run the CodeCompress **update/rebuild index** tool **first**, before any query tools. This ensures the index reflects the latest state of the codebase (recent commits, new files, renamed types). Do not proceed until the index update completes successfully.

### 0b. Discover the Codebase

Use CodeCompress query tools to explore the codebase and build a working mental model:

- Query for the **top-level architecture** — projects, namespaces, key entry points
- Query for **types and interfaces** referenced in the input document (mini-plan, story, or issue)
- Query for **existing patterns** — how similar features were implemented, DI registration conventions, test structure
- Query for **dependencies** — verify prerequisite types/files actually exist before assuming they do

Only after this discovery phase should you proceed to read the input document in detail.

### 0c. Load Input Document

1. Read the input at `$ARGUMENTS` (mini-plan, user story, or GitHub issue — see [Input Detection](#input-detection) above)
2. Read `CLAUDE.md` for project conventions
3. If the input is a **mini-plan**: Read the **Dependencies** section — cross-reference against CodeCompress index results to verify all prerequisite features exist. If dependencies are missing, stop and report.
4. If the input is a **user story or issue**: Proceed to [Deriving a Plan from a Story/Issue](#deriving-a-plan-from-a-storyissue).
5. Read the **Files to Create/Modify** section (mini-plan) or your derived file list (story/issue) to understand the full scope.
6. **Look up documentation** for any libraries/frameworks referenced in the scope using Context7 or Ref MCPs. Cache key API patterns before starting implementation.

## Deriving a Plan from a Story/Issue

When the input is a user story or GitHub issue rather than a structured mini-plan, you must produce the equivalent plan yourself before entering the TDD cycle.

1. **Understand the ask**: Identify the user-facing outcome, acceptance criteria, and any constraints or edge cases described in the story/issue.
2. **Codebase mapping** (using CodeCompress query results from Step 0b): Determine which existing files, types, and interfaces are affected. Identify where new code should live based on existing project structure and conventions from `CLAUDE.md`.
3. **Produce a lightweight implementation plan** containing:
    - **Files to Create/Modify** — full paths, with a brief description of changes per file
    - **Interfaces/Types** — new or modified signatures
    - **Test plan** — test classes to create, key scenarios (happy path, edge cases, error cases, boundary conditions)
    - **Acceptance Criteria** — transcribed from the story/issue, plus any additional technical criteria you identify
    - **Security considerations** — any security requirements triggered by the scope (path handling, SQL, FTS5, output sanitization)
4. **Present the plan briefly** in your response before proceeding — this gives visibility into your approach. You do not need to write a separate file; an inline summary is sufficient.
5. Proceed to Step 1 with this derived plan as your reference.

## Step 1: Plan the Implementation Order

Before writing any code, determine the correct implementation order:

- Which types/interfaces need to exist before others?
- Which tests should be written first?
- Are there any shared helpers or base classes needed?

State your implementation order briefly, then proceed.

## Step 2: TDD Cycle (Mandatory)

For **every** deliverable in the plan (mini-plan or derived plan), follow this strict cycle:

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
- Include edge cases, error cases, and boundary conditions from the plan's test section

### 2b. Verify Tests Fail

- Run: `dotnet test --filter "FullyQualifiedName~TestClassName"` to confirm tests fail (red phase)
- If tests pass without implementation, the tests are wrong — fix them

### 2c. Write Minimum Implementation

- Write the **minimum code** to make tests pass — no more
- Follow the signatures, types, and patterns specified in the plan

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
- **Parallel implementation**: When the plan has independent components (e.g., multiple CRUD methods, multiple test classes), launch agents in parallel to implement them.

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
3. **Acceptance criteria**: Go through every checkbox in the plan's **Acceptance Criteria** section and verify each one is met
4. **Coverage check**: Verify test coverage meets targets from CLAUDE.md (Parsers 95%+, Storage 90%+, IndexEngine 90%+, MCP Tools 85%+)
5. **Security audit**: Verify no parameterized query violations, no path traversal gaps, no unsanitized output

## Step 6: Summary

When complete, provide:

- List of files created/modified
- Test count and pass/fail status
- Any deviations from the plan (with justification)
- Any open items or follow-up needed

## Key Reference

- [CLAUDE.md](../../../CLAUDE.md) — Project conventions, architecture, security requirements
- [PRD](../../../docs/code-compress-prd.md) — Full product requirements (for context, not implementation)
