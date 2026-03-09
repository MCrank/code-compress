# 001 — Server NuGet Packaging and MCP Registry Metadata

## Summary

Configure `CodeCompress.Server` as the primary distributable NuGet package with `PackAsTool`, `McpServer` package type, and `.mcp/server.json` registry metadata. This makes the MCP server discoverable on NuGet.org and installable via `dnx` (the .NET 10 equivalent of `npx`).

## Dependencies

- **Feature 07** — MCP server host with stdio transport (already exists).
- **Feature 13-001** — CI workflow (for build validation).

## Scope

### 1. Server .csproj Packaging Properties

Add the following to `src/CodeCompress.Server/CodeCompress.Server.csproj`:

| Property | Value | Purpose |
|---|---|---|
| `PackAsTool` | `true` | Marks project as a dotnet tool |
| `ToolCommandName` | `codecompress-server` | Command name after install |
| `PackageId` | `CodeCompress.Server` | NuGet package identifier |
| `PackageType` | `McpServer` | NuGet MCP server package type for discoverability |
| `Description` | MCP server description | NuGet description |
| `Authors` | Project authors | |
| `PackageLicenseExpression` | `MIT` | |
| `PackageProjectUrl` | Repository URL | |
| `RepositoryUrl` | Repository URL | |
| `PackageTags` | `mcp;mcp-server;code-index;ai;codebase;symbols` | |
| `PackageReadmeFile` | `README.md` | Include README in package |

### 2. MCP Registry Metadata (`.mcp/server.json`)

Create `.mcp/server.json` in the Server project directory, packed into the NuGet package. This file follows the [MCP Registry schema](https://github.com/modelcontextprotocol/registry/blob/main/docs/reference/server-json/generic-server-json.md) and is used by NuGet.org to generate configuration snippets for VS Code, Claude Code, etc.

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json",
  "name": "io.github.mcrank/code-compress",
  "description": "MCP server that indexes codebases and provides AI agents with compressed, surgical access to code symbols via SQLite-backed persistent indexes.",
  "version": "0.1.0",
  "packages": [
    {
      "registryType": "nuget",
      "registryBaseUrl": "https://api.nuget.org",
      "identifier": "CodeCompress.Server",
      "version": "0.1.0",
      "transport": {
        "type": "stdio"
      },
      "packageArguments": [],
      "environmentVariables": []
    }
  ],
  "repository": {
    "url": "https://github.com/MCrank/code-compress",
    "source": "github"
  }
}
```

The `.mcp/server.json` must be included in the NuGet package via an `<ItemGroup>` in the `.csproj`:

```xml
<ItemGroup>
  <None Include=".mcp\server.json" Pack="true" PackagePath=".mcp\" />
  <None Include="..\..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

### 3. Self-Contained Build (Recommended)

Per Microsoft's guidance, MCP servers should be self-contained to avoid runtime dependency issues. Consider adding:

```xml
<PublishSelfContained>true</PublishSelfContained>
<RuntimeIdentifiers>win-x64;linux-x64;osx-x64;osx-arm64</RuntimeIdentifiers>
```

This is a stretch goal — the framework-dependent package works with `dnx` since `dnx` ships with the .NET SDK (which includes the runtime). Self-contained can be added later if needed.

### 4. Verify Pack Output

- `dotnet pack src/CodeCompress.Server/CodeCompress.Server.csproj -c Release` produces a valid `.nupkg`
- The `.nupkg` contains `.mcp/server.json`
- The package has `PackageType` of `McpServer`
- `dotnet tool install --global --add-source ./nupkg CodeCompress.Server` works and `codecompress-server` starts the MCP server

## Acceptance Criteria

- [ ] `CodeCompress.Server.csproj` has `PackAsTool=true` and `ToolCommandName=codecompress-server`.
- [ ] `PackageType` is set to `McpServer`.
- [ ] Package metadata (description, license, tags, URLs) is populated.
- [ ] `.mcp/server.json` exists and follows the MCP Registry schema.
- [ ] `.mcp/server.json` is included in the packed `.nupkg`.
- [ ] `dotnet pack` produces a valid `.nupkg` for the Server.
- [ ] `dotnet build CodeCompress.slnx` succeeds with zero warnings.
- [ ] The packed tool can be installed and starts the MCP server via stdio.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `src/CodeCompress.Server/.mcp/server.json` | MCP Registry metadata |

### Modify

| File | Description |
|---|---|
| `src/CodeCompress.Server/CodeCompress.Server.csproj` | Add packaging properties and server.json inclusion |

## Out of Scope

- Self-contained / AOT compilation (stretch goal for later).
- Release workflow changes (covered in 14-002).
- Client configuration docs (covered in 14-003).

## Notes / Decisions

1. **`dnx` vs `dotnet tool install`.** The `dnx` command (new in .NET 10) is the recommended way to run NuGet-based MCP servers. It downloads and executes in one shot without permanent installation. `dotnet tool install --global` still works for users who prefer a persistent install.
2. **Package type `McpServer`.** NuGet.org uses this to surface the package in MCP-specific search results and display configuration snippets on the package page.
3. **`server.json` versioning.** The `version` field in `server.json` should match the package version. During release, the workflow can update this or it can use a placeholder that gets overridden by `/p:Version`.
