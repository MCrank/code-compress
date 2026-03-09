# 003 — Client Configuration Documentation

## Summary

Update the README and release process docs with comprehensive instructions for installing and configuring CodeCompress as an MCP server in Claude Code, VS Code / GitHub Copilot, and other MCP clients. Also add a project-scoped `.mcp.json` example for team sharing.

## Dependencies

- **Feature 14-001** — Server NuGet packaging (so we know the package name and command).

## Scope

### 1. README Installation Section Update

Replace the current "Setup" section in `README.md` with updated instructions covering all installation methods:

#### From NuGet (recommended)

```bash
# Via dnx (no install needed — downloads and runs in one shot)
dnx CodeCompress.Server --yes

# Via dotnet tool install (persistent global install)
dotnet tool install -g CodeCompress.Server
```

#### From Source (development)

```bash
git clone https://github.com/MCrank/code-compress.git
cd code-compress
dotnet run --project src/CodeCompress.Server
```

### 2. Client Configuration Examples

#### Claude Code

```bash
# One-liner setup
claude mcp add --transport stdio codecompress -- dnx CodeCompress.Server --yes

# Or from source
claude mcp add --transport stdio codecompress -- dotnet run --project /path/to/src/CodeCompress.Server
```

#### VS Code / GitHub Copilot (`.vscode/mcp.json`)

```json
{
  "servers": {
    "codecompress": {
      "type": "stdio",
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

#### Project-Scoped Sharing (`.mcp.json` at project root)

```json
{
  "mcpServers": {
    "codecompress": {
      "type": "stdio",
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

This file can be committed to the repo so all team members get the MCP server automatically.

#### Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "codecompress": {
      "command": "dnx",
      "args": ["CodeCompress.Server", "--yes"]
    }
  }
}
```

#### From Source (any client)

For development, use `dotnet run` instead of `dnx`:

```json
{
  "servers": {
    "codecompress": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/src/CodeCompress.Server"]
    }
  }
}
```

### 3. Windows Note

On native Windows (not WSL), stdio MCP servers that use `dnx` may require the `cmd /c` wrapper:

```bash
claude mcp add --transport stdio codecompress -- cmd /c dnx CodeCompress.Server --yes
```

### 4. CLI Tool Documentation

Add a separate section for the optional CLI tool:

```bash
dotnet tool install -g CodeCompress
codecompress index /path/to/project
codecompress outline /path/to/project
```

### 5. Update Release Process Doc

Update `docs/release-process.md`:
- Add "Installation for MCP Clients" section with client-specific examples
- Update "Installation for End Users" to distinguish between MCP server and CLI
- Add note about `dnx` requiring .NET 10 SDK

## Acceptance Criteria

- [ ] README has updated installation instructions for `dnx` and `dotnet tool install`.
- [ ] README has configuration examples for Claude Code, VS Code, Claude Desktop.
- [ ] README includes Windows-specific notes.
- [ ] README distinguishes between MCP server (primary) and CLI (optional).
- [ ] `docs/release-process.md` updated with MCP client installation section.
- [ ] All configuration JSON examples are valid and tested.
- [ ] `dotnet build CodeCompress.slnx` still succeeds with zero warnings.

## Files to Create/Modify

### Modify

| File | Description |
|---|---|
| `README.md` | Update Setup/Installation sections with MCP client configuration |
| `docs/release-process.md` | Add MCP client installation section, update user installation |

## Out of Scope

- Registering with the central MCP Registry (not yet live for public submissions).
- OAuth or HTTP transport configuration (CodeCompress uses stdio only).
- Plugin packaging for Claude Code plugins.

## Notes / Decisions

1. **`dnx` is the primary install method.** It's the recommended approach from Microsoft for NuGet-based MCP servers. It requires .NET 10 SDK but avoids the need for `dotnet tool install` / `dotnet tool update` lifecycle management.
2. **`dotnet tool install` as fallback.** For users who prefer a persistent global install, the traditional `dotnet tool install -g` still works since the package has `PackAsTool=true`.
3. **Project-scoped `.mcp.json`.** This is the recommended way for teams to share MCP server configuration. It's checked into version control and Claude Code / VS Code pick it up automatically.
4. **From-source configuration.** Always document the `dotnet run --project` approach for contributors and developers working on CodeCompress itself.
