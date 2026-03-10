# 001 — PathValidator: OWASP Path Traversal Prevention

## Summary

Implement `PathValidator`, a critical security component that prevents path traversal attacks (OWASP A01) across all MCP tool parameters that accept file paths. Every file system access in CodeCompress must pass through PathValidator, which canonicalizes the input path and verifies it resolves within the declared project root. This is a hard security boundary — no file outside the project root may be read, regardless of how the path is constructed.

## Dependencies

- **Feature 01** (Project Scaffold) — solution structure, build configuration, shared props
- **Feature 02** (Core Models and Interfaces) — exception types if custom validation exceptions are defined there

## Scope

### 1. PathValidator Static Class

**File:** `src/CodeCompress.Core/Validation/PathValidator.cs`

A static class (no instance state needed) exposing three public methods:

#### `ValidatePath(string inputPath, string projectRoot) → string`

- Accepts an absolute path and a project root.
- Returns the canonicalized safe path if valid.
- Throws `ArgumentException` (or a custom `PathValidationException`) if the path escapes the root or is otherwise invalid.
- Steps:
  1. Reject null, empty, or whitespace inputs immediately.
  2. Canonicalize both `inputPath` and `projectRoot` via `Path.GetFullPath()` to resolve `.`, `..`, double slashes, mixed separators, and trailing slashes.
  3. Normalize the project root to include a trailing directory separator so that `/project` does not match `/project-other`.
  4. Perform a starts-with check: the canonicalized input must start with the canonicalized root.
  5. On Windows, use `StringComparison.OrdinalIgnoreCase`; on Linux/macOS, use `StringComparison.Ordinal`.
  6. If the path is exactly the root directory, allow it.

#### `ValidateRelativePath(string relativePath, string projectRoot) → string`

- Resolves a relative path against the project root using `Path.GetFullPath(relativePath, projectRoot)`.
- Delegates to `ValidatePath` for the actual security check.
- Returns the canonicalized absolute path.

#### `IsWithinRoot(string candidatePath, string projectRoot) → bool`

- Non-throwing variant of `ValidatePath`.
- Returns `true` if the candidate resolves within root, `false` otherwise.
- Catches exceptions from invalid inputs and returns `false`.

### 2. Edge Cases and Platform Handling

| Concern | Approach |
|---------|----------|
| **Null / empty / whitespace** | Throw `ArgumentException` with a clear message. Do not propagate `NullReferenceException`. |
| **`..` traversal** | `Path.GetFullPath()` resolves `..` segments before the starts-with check, so `"/project/../../../etc/passwd"` becomes `"/etc/passwd"` and fails. |
| **Symlinks** | After canonicalization, if the OS resolves a symlink to a target outside root, the starts-with check rejects it. On .NET, `Path.GetFullPath()` does not resolve symlinks by default; use `new FileInfo(path).LinkTarget` or `File.ResolveLinkTarget()` when the path exists, and re-validate the resolved target. If the file does not yet exist, accept the canonicalized path (no symlink to resolve). |
| **Case sensitivity** | Use `OperatingSystem.IsWindows()` to select the comparison: `OrdinalIgnoreCase` on Windows, `Ordinal` on Linux/macOS. |
| **Trailing / double slashes** | `Path.GetFullPath()` normalizes these. Ensure the root always ends with the directory separator character for the starts-with check. |
| **Mixed separators** | `Path.GetFullPath()` normalizes `/` vs `\` per platform. |
| **UNC paths on Windows** | Reject any path starting with `\\` (or `//` before normalization) unless the project root itself is a UNC path. UNC paths to network shares are an escalation vector. |
| **Null bytes** | Reject any path containing `\0`. .NET's `Path.GetFullPath()` already throws on null bytes, but validate explicitly for a clear error message. |
| **Special characters** | Allow valid filesystem characters. `Path.GetFullPath()` throws on invalid path characters — let that propagate with a descriptive wrapper. |
| **Very long paths** | Allow paths up to the OS limit. If `Path.GetFullPath()` throws `PathTooLongException`, wrap with context. |

### 3. Test Coverage

**File:** `tests/CodeCompress.Core.Tests/Validation/PathValidatorTests.cs`

All tests use TUnit with async fluent assertions.

#### Happy Path Tests

- Valid absolute path within project root returns canonicalized path.
- Valid relative path (`"src/Foo.cs"`) resolves to `{root}/src/Foo.cs`.
- Path exactly equal to root is accepted.
- Subdirectory path is accepted.
- `IsWithinRoot` returns `true` for valid paths.

#### Traversal Attack Tests

- `"/project/../../../etc/passwd"` — rejected.
- `"/project/src/../../outside"` — rejected.
- Relative path `"../../etc/passwd"` — rejected.
- Multiple `..` segments that escape root — rejected.
- `..` that stays within root (e.g., `"/project/src/../lib/Foo.cs"`) — accepted.

#### Input Validation Tests

- `null` input — throws `ArgumentException`.
- Empty string — throws `ArgumentException`.
- Whitespace-only string — throws `ArgumentException`.
- `null` project root — throws `ArgumentException`.

#### Null Byte / Encoding Tests

- Path containing `\0` — rejected.
- Paths with URL-encoded characters (`%2e%2e`) are treated as literal characters (not decoded), which is correct — the filesystem does not interpret URL encoding.

#### Platform-Specific Tests

- **Windows case sensitivity:** `"/Project/SRC/Foo.cs"` with root `"/project"` — accepted on Windows, rejected on Linux (use conditional test or parameterized with `OperatingSystem.IsWindows()` guard).
- **UNC path:** `"\\\\server\\share\\file"` — rejected (unless root is also UNC).
- **Mixed separators:** `"/project\\src/Foo.cs"` — normalized and accepted.

#### Edge Case Tests

- Trailing slash on root does not affect validation.
- Double slashes in path (`"/project//src//Foo.cs"`) — normalized and accepted.
- Path that is a prefix of root but not a child (`"/project-other/file"` with root `"/project"`) — rejected (the trailing-separator trick prevents this).
- Very long path — accepted up to OS limit, appropriate error beyond.
- `IsWithinRoot` returns `false` for all invalid cases (does not throw).

#### Parameterized Tests

Use `[Arguments(...)]` for traversal vectors:

```csharp
[Test]
[Arguments("/../../../etc/passwd")]
[Arguments("/../../windows/system32/config/sam")]
[Arguments("/../")]
[Arguments("/./../../outside")]
public async Task ValidatePath_TraversalAttempts_Throws(string maliciousSegment)
{
    // Arrange — prepend project root to malicious segment
    // Act & Assert — should throw
}
```

## Acceptance Criteria

- [ ] `PathValidator` class exists at `src/CodeCompress.Core/Validation/PathValidator.cs`
- [ ] `ValidatePath` canonicalizes and validates absolute paths against a project root
- [ ] `ValidateRelativePath` resolves relative paths and delegates to `ValidatePath`
- [ ] `IsWithinRoot` provides a non-throwing boolean check
- [ ] Null, empty, and whitespace inputs throw `ArgumentException`
- [ ] Paths containing `..` that escape root are rejected
- [ ] Paths containing null bytes are rejected
- [ ] UNC paths are rejected (unless root is UNC)
- [ ] Case comparison is OS-aware (case-insensitive on Windows, case-sensitive on Linux/macOS)
- [ ] The trailing-separator normalization prevents prefix false positives (`/project` vs `/project-other`)
- [ ] Symlinks that resolve outside root are rejected (when the path exists on disk)
- [ ] All test cases in `PathValidatorTests.cs` pass
- [ ] Code coverage on `PathValidator.cs` is 95% or above
- [ ] Zero build warnings (SonarAnalyzer, nullable, code style)
- [ ] No string concatenation in error messages that echoes back raw user input (prompt injection prevention)

## Files to Create/Modify

| File | Action | Notes |
|------|--------|-------|
| `src/CodeCompress.Core/Validation/PathValidator.cs` | Create | Static class with three public methods |
| `tests/CodeCompress.Core.Tests/Validation/PathValidatorTests.cs` | Create | Full test suite covering all cases above |

## Out of Scope

- Integrating PathValidator into MCP tool methods (happens in Features 07-10 when tools are built).
- File content reading or modification — PathValidator only validates paths, it does not perform I/O.
- Network path (UNC) support as a feature — UNC paths are explicitly rejected for security.
- Symbolic link creation or manipulation — only detection/resolution of existing symlinks.
- Custom exception types — use `ArgumentException` unless Feature 02 defines a `PathValidationException`. Revisit if needed.

## Notes / Decisions

1. **Static class, not a service** — PathValidator has no dependencies and no state. Making it static avoids unnecessary DI registration and keeps call sites simple (`PathValidator.ValidatePath(...)` rather than injecting an interface). If future requirements add configuration (e.g., allowed UNC roots), it can be refactored to an instance behind `IPathValidator`.
2. **Trailing directory separator trick** — Comparing `candidatePath.StartsWith(rootWithSeparator)` prevents the classic false positive where `/project-other/evil.cs` matches root `/project`. The root is always normalized to end with `Path.DirectorySeparatorChar` before comparison.
3. **Symlink handling is best-effort** — On .NET, resolving symlinks portably is non-trivial. The implementation should use `File.ResolveLinkTarget(path, returnFinalTarget: true)` when the path exists and is a symlink, then re-validate the resolved target. If the file does not exist (e.g., validating a path before creating a database), skip symlink resolution — canonicalization via `Path.GetFullPath()` is sufficient.
4. **No URL decoding** — The filesystem layer does not interpret `%2e%2e` as `..`. Decoding URL-encoded paths would be incorrect and could introduce a bypass if a future layer pre-decodes. PathValidator operates on raw filesystem paths only.
5. **Error messages must not echo user input** — Per the prompt injection prevention requirement, exception messages should describe what went wrong (e.g., "Path resolves outside the project root") without including the actual path value in freeform text that could be surfaced to an AI agent. The path can be included in structured error data if needed for debugging.
