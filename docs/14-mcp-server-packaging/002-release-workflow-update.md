# 002 — Release Workflow Update for Server Package

## Summary

Update the release workflow to pack and publish the `CodeCompress.Server` NuGet package (the MCP server) as the primary release artifact, alongside the existing `CodeCompress` CLI tool package.

## Dependencies

- **Feature 14-001** — Server NuGet packaging configuration.
- **Feature 13-002** — Existing release workflow.

## Scope

### 1. Update Release Workflow (`.github/workflows/release.yml`)

Modify the release workflow to pack and publish both packages:

| Step | Command | Notes |
|---|---|---|
| Pack Server | `dotnet pack src/CodeCompress.Server/CodeCompress.Server.csproj -c Release -o ./nupkg /p:Version=$VERSION` | Primary MCP server package |
| Pack CLI | `dotnet pack src/CodeCompress.Cli/CodeCompress.Cli.csproj -c Release -o ./nupkg /p:Version=$VERSION` | Secondary CLI tool |
| Publish all | `dotnet nuget push ./nupkg/*.nupkg ...` | Pushes both packages |
| GitHub Release | `gh release create ... ./nupkg/*.nupkg` | Attaches both as assets |

### 2. Version Consistency

Both packages should use the same version derived from the Git tag. The `server.json` version field should also be updated during pack — either via a build step that patches the file or by accepting that it contains a placeholder updated at release time.

Options for `server.json` version sync:
- **Option A:** Use a pre-pack step to `sed`/replace the version in `server.json` before packing.
- **Option B:** Accept that `server.json` version is approximate and update it manually before tagging.
- **Option C:** Use MSBuild to inject the version into the file at pack time.

Recommended: **Option A** — simple `sed` replacement in the workflow.

### 3. Verify Both Packages

Add a verification step after pack:
- List contents of `./nupkg/` to confirm both `.nupkg` files exist
- Optionally validate the Server package contains `.mcp/server.json`

## Acceptance Criteria

- [ ] Release workflow packs both `CodeCompress.Server` and `CodeCompress` (CLI).
- [ ] Both packages use the version from the Git tag.
- [ ] Both packages are pushed to NuGet.org.
- [ ] Both packages are attached to the GitHub Release.
- [ ] `server.json` version matches the release version.
- [ ] Workflow passes end-to-end on a test tag push.

## Files to Create/Modify

### Modify

| File | Description |
|---|---|
| `.github/workflows/release.yml` | Add Server pack step, update push to include both packages |

## Out of Scope

- CI workflow changes (CI already builds the full solution).
- Self-contained / platform-specific packaging.
- NuGet package signing.

## Notes / Decisions

1. **Single `dotnet nuget push` with wildcard.** Since both `.nupkg` files land in `./nupkg/`, the existing `./nupkg/*.nupkg` glob already pushes both. The main change is adding the second `dotnet pack` step.
2. **GitHub Release assets.** Both packages are attached so users can download either one directly from the release page.
