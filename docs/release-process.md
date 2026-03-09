# CodeCompress Release Process

This document covers the full CI/CD pipeline and release process for CodeCompress.

## Table of Contents

- [Overview](#overview)
- [CI Pipeline](#ci-pipeline)
- [Release Pipeline](#release-pipeline)
- [How to Cut a Release](#how-to-cut-a-release)
- [Version Scheme](#version-scheme)
- [One-Time Setup](#one-time-setup)
- [Installation for End Users](#installation-for-end-users)
- [Troubleshooting](#troubleshooting)

---

## Overview

CodeCompress uses two GitHub Actions workflows:

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **CI** | `.github/workflows/ci.yml` | Push to `develop`, PRs targeting `develop` | Build, test, and validate on every change |
| **Release** | `.github/workflows/release.yml` | Push of a `v*` tag (e.g., `v1.0.0`) | Build, test, pack, publish to NuGet, create GitHub Release |

```
feature/* ──> develop ──────PR──────> main ──tag v*──> NuGet.org
                |                      |                  ^
                |  push / PR           |                  |
                v                      |            [Release Workflow]
           [CI Workflow]               |             - Build (Ubuntu)
            - Build (3 OS)             |             - Test
            - Test (3 OS)             back-          - Pack .nupkg
            - Upload TRX             merge           - Publish to NuGet
              artifacts                |             - Create GitHub Release
                ^                      |
                |                      |
                +──────────────────────+
```

---

## CI Pipeline

**Workflow:** `.github/workflows/ci.yml`

### Triggers

- Every push to the `develop` branch
- Every pull request targeting `develop`

### What It Does

The CI workflow runs on a **3-OS matrix** in parallel to verify cross-platform compatibility:

| Runner | Purpose |
|--------|---------|
| `ubuntu-latest` | Primary Linux validation |
| `windows-latest` | Windows compatibility |
| `macos-latest` | macOS compatibility |

All three runners execute independently with `fail-fast: false`, meaning a failure on one OS does not cancel the others.

### Steps (per OS)

| # | Step | Command | Notes |
|---|------|---------|-------|
| 1 | Checkout | `actions/checkout@v4` | Full clone |
| 2 | Setup .NET | `actions/setup-dotnet@v4` | .NET SDK `10.0.x` |
| 3 | Restore | `dotnet restore CodeCompress.slnx` | NuGet package restore |
| 4 | Build | `dotnet build CodeCompress.slnx --no-restore -c Release` | Zero-warning enforcement |
| 5 | Test | `dotnet test CodeCompress.slnx --no-build -c Release` | TRX output format |
| 6 | Upload | `actions/upload-artifact@v4` | Test results as `test-results-{os}` |

### Static Analysis

There is no separate linting or analysis step. The project's `Directory.Build.props` enforces:

- `TreatWarningsAsErrors=true` — any warning fails the build
- `AnalysisLevel=latest-all` — all .NET analyzers enabled
- `SonarAnalyzer.CSharp` — additional static analysis rules

This means the **Build** step acts as the quality gate. If any analyzer rule is violated, the build fails.

### Test Results

Test results are saved in TRX format and uploaded as GitHub Actions artifacts. They persist for the default retention period (90 days) and can be downloaded from the Actions run page to debug failures.

Artifacts are named per-OS:
- `test-results-ubuntu-latest`
- `test-results-windows-latest`
- `test-results-macos-latest`

---

## Release Pipeline

**Workflow:** `.github/workflows/release.yml`

### Trigger

Push of a Git tag matching `v*` (e.g., `v1.0.0`, `v0.2.0-preview.1`).

### Runner

Single platform: `ubuntu-latest`. The dotnet tool package is platform-independent (managed assemblies), so cross-platform builds are unnecessary for release. The CI workflow already validates all three platforms on every change.

### Steps

| # | Step | Command | Notes |
|---|------|---------|-------|
| 1 | Checkout | `actions/checkout@v4` | |
| 2 | Setup .NET | `actions/setup-dotnet@v4` | .NET SDK `10.0.x` |
| 3 | Restore | `dotnet restore CodeCompress.slnx` | |
| 4 | Build | `dotnet build CodeCompress.slnx --no-restore -c Release` | Zero-warning gate |
| 5 | Test | `dotnet test CodeCompress.slnx --no-build -c Release` | All tests must pass |
| 6 | Determine version | `echo "VERSION=${GITHUB_REF#refs/tags/v}" >> $GITHUB_ENV` | Strips `v` prefix from tag |
| 7 | Pack | `dotnet pack ... -o ./nupkg /p:Version=$VERSION` | Produces `.nupkg` |
| 8 | Publish to NuGet | `dotnet nuget push ... --api-key ${{ secrets.NUGET_API_KEY }}` | Pushes to NuGet.org |
| 9 | Create GitHub Release | `gh release create ${{ github.ref_name }} ./nupkg/*.nupkg --generate-notes` | Attaches `.nupkg` as asset |

### Safety Gates

The release workflow will **not publish** if:

- The build produces any warnings (zero-warning enforcement)
- Any test fails (test step runs before pack/publish)
- The `NUGET_API_KEY` secret is missing or invalid

Each step depends on the previous one succeeding. If build or test fails, pack/publish/release steps never execute.

### What Gets Published

The `dotnet pack` step packages `src/CodeCompress.Cli` as a [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools):

| Property | Value |
|----------|-------|
| Package ID | `CodeCompress` |
| Tool command | `codecompress` |
| License | MIT |
| NuGet URL | https://www.nuget.org/packages/CodeCompress |
| Source | https://github.com/MCrank/code-compress |

The GitHub Release includes:
- Auto-generated release notes (commit list since previous tag)
- The `.nupkg` file attached as a downloadable asset

---

## How to Cut a Release

### Branch Flow

CodeCompress uses a `develop` → `main` branching model. All feature work lands on `develop`. Releases are cut from `main` after merging `develop` into it via a pull request.

```
feature branches → develop (CI runs here) → PR to main → tag on main → Release workflow
```

The full release sequence:

1. **Develop** — Feature branches merge into `develop`. CI validates every push and PR.
2. **PR to main** — When `develop` is release-ready, open a PR from `develop` to `main`. This is the release review gate — use it to verify the changelog, run a final review, and confirm the version number.
3. **Merge** — Merge the PR into `main`.
4. **Tag** — Create a version tag on `main` and push it. This triggers the release workflow.
5. **Back-merge** — Merge `main` back into `develop` so the tag commit is on both branches.

### Standard Release

```bash
# 1. Ensure develop is green (CI passing, all tests pass)
git checkout develop
git pull origin develop

# 2. Verify locally (optional but recommended)
dotnet build CodeCompress.slnx -c Release
dotnet test --solution CodeCompress.slnx

# 3. Create a PR from develop to main
gh pr create --base main --head develop \
  --title "Release v1.0.0" \
  --body "Merge develop into main for v1.0.0 release."

# 4. After PR review and merge, tag main
git checkout main
git pull origin main
git tag v1.0.0
git push origin v1.0.0

# 5. Back-merge main into develop to keep branches in sync
git checkout develop
git merge main
git push origin develop
```

The release workflow triggers automatically on the tag push and handles build, test, pack, NuGet publish, and GitHub Release creation.

### Pre-Release

For pre-releases you may tag directly on `develop` without merging to `main`, since the code is not yet considered stable:

```bash
git checkout develop
git pull origin develop
git tag v0.2.0-preview.1
git push origin v0.2.0-preview.1
```

NuGet treats any version with a `-suffix` as a pre-release. Pre-release packages:
- Are not shown by default in NuGet package manager
- Require `--prerelease` flag to install
- Are listed separately on the NuGet.org package page

### Patch Release

For urgent fixes that need to ship without the full contents of `develop`:

```bash
# 1. Create a hotfix branch from main
git checkout main
git pull origin main
git checkout -b hotfix/v1.0.1

# 2. Make the fix, commit, push, and open a PR to main
gh pr create --base main --head hotfix/v1.0.1 \
  --title "Fix: description of the fix" \
  --body "Hotfix for v1.0.0."

# 3. After merge, tag main
git checkout main
git pull origin main
git tag v1.0.1
git push origin v1.0.1

# 4. Back-merge into develop
git checkout develop
git merge main
git push origin develop
```

### Deleting a Tag (if something goes wrong before publish)

If you push a tag by mistake and need to cancel before the workflow completes:

```bash
# Cancel the workflow run in GitHub Actions UI first, then:
git tag -d v1.0.0
git push origin --delete v1.0.0
```

> **Note:** If the NuGet package has already been published, it cannot be deleted — only unlisted. See [Troubleshooting](#troubleshooting).

---

## Version Scheme

CodeCompress follows [Semantic Versioning](https://semver.org/):

```
MAJOR.MINOR.PATCH[-PRERELEASE]
```

| Component | When to increment |
|-----------|-------------------|
| **MAJOR** | Breaking changes to MCP tool contracts, CLI command signatures, or database schema |
| **MINOR** | New features (new MCP tools, new language parsers, new CLI commands) |
| **PATCH** | Bug fixes, performance improvements, dependency updates |
| **PRERELEASE** | Preview/beta releases (e.g., `1.0.0-preview.1`, `2.0.0-rc.1`) |

### Tag Format

Tags **must** use the `v` prefix: `v1.0.0`, `v0.3.0-preview.2`.

The release workflow strips the `v` to derive the NuGet package version:
- Tag `v1.0.0` → Package version `1.0.0`
- Tag `v0.2.0-preview.1` → Package version `0.2.0-preview.1`

### Examples

| Change | Version bump | Tag |
|--------|-------------|-----|
| Add Python parser | Minor | `v1.1.0` |
| Fix FTS5 search crash | Patch | `v1.0.1` |
| Rename `get_symbol` tool parameter | Major | `v2.0.0` |
| Early testing release | Pre-release | `v0.1.0-preview.1` |

---

## One-Time Setup

Before your first release, configure the following:

### 1. NuGet API Key

1. Go to https://www.nuget.org/account/apikeys
2. Create a new API key:
   - **Key name:** `codecompress-github-actions` (or similar)
   - **Package owner:** Your NuGet account
   - **Glob pattern:** `CodeCompress`
   - **Scopes:** Push new packages and package versions
3. Copy the generated key

### 2. GitHub Repository Secret

1. Go to your repository: **Settings → Secrets and variables → Actions**
2. Click **New repository secret**
3. Name: `NUGET_API_KEY`
4. Value: Paste the NuGet API key from step 1
5. Click **Add secret**

### 3. Verify Permissions

The release workflow uses `permissions: contents: write` to create GitHub Releases. This is provided by the default `GITHUB_TOKEN` — no additional setup needed.

---

## Installation for End Users

Once a release is published, users can install CodeCompress as a global .NET tool:

### Install

```bash
dotnet tool install -g CodeCompress
```

### Install a specific version

```bash
dotnet tool install -g CodeCompress --version 1.0.0
```

### Install a pre-release

```bash
dotnet tool install -g CodeCompress --prerelease
```

### Update to latest

```bash
dotnet tool update -g CodeCompress
```

### Uninstall

```bash
dotnet tool uninstall -g CodeCompress
```

### Verify installation

```bash
codecompress
```

This should print the usage/help text listing all available commands.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

---

## Troubleshooting

### CI is failing on one OS but passing on others

Check the test results artifact for the failing OS. Common causes:
- Path separator differences (`/` vs `\`) — use `Path.Combine` and `Path.DirectorySeparatorChar`
- Line ending differences — ensure parsers handle both `\n` and `\r\n`
- File system case sensitivity — Linux is case-sensitive, Windows/macOS are not

### Release workflow fails at "Publish to NuGet"

- **`403 Forbidden`**: The `NUGET_API_KEY` secret is invalid, expired, or doesn't have push permissions for the `CodeCompress` package ID. Regenerate the key on NuGet.org and update the GitHub secret.
- **`409 Conflict`**: A package with that exact version already exists on NuGet.org. You cannot overwrite published versions. Increment the version and create a new tag.

### Need to "undo" a published NuGet package

NuGet does not support deleting published packages. You can **unlist** a version:

```bash
dotnet nuget delete CodeCompress 1.0.0 --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY --non-interactive
```

This hides the version from search results but does not remove it. Users who already reference that exact version can still restore it.

To fix a bad release, publish a new patch version instead.

### Release workflow fails at "Create GitHub Release"

- Ensure the workflow has `permissions: contents: write`
- If a release for that tag already exists, the `gh release create` command will fail. Delete the existing release in the GitHub UI and re-run the workflow, or create the release manually.

### `dotnet tool install` fails for users

- **`.NET 10 SDK not found`**: The user needs .NET 10 SDK installed. CodeCompress targets `net10.0`.
- **`Package not found`**: The NuGet package index takes a few minutes to update after publishing. Wait and retry.
- **Pre-release not found**: Pre-release packages require the `--prerelease` flag.
