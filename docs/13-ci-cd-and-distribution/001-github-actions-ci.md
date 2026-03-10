# 001 — GitHub Actions CI Workflow

## Summary

Automated continuous integration pipeline that builds the solution, runs all tests across multiple platforms, and enforces zero-warning compilation. Triggered on every push to `main` and on all pull requests. Ensures the codebase stays green with SonarAnalyzer static analysis enforced at build time.

## Dependencies

- **All features** — CI runs the full `dotnet build` + `dotnet test` across the entire solution, so all projects must be present and buildable.

## Scope

### 1. CI Workflow (`.github/workflows/ci.yml`)

| Aspect | Detail |
|---|---|
| Trigger | `push` to `main` branch; `pull_request` targeting `main` |
| Matrix | `ubuntu-latest` (primary), `windows-latest`, `macos-latest` |
| .NET SDK | `10.0.x` (matching `global.json` constraint) |
| Fail-fast | `false` — run all matrix entries even if one fails |

### 2. Workflow Steps

| Step | Command/Action | Notes |
|---|---|---|
| Checkout | `actions/checkout@v4` | Full clone for accurate diffs |
| Setup .NET | `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` | Matches `global.json` rollForward: latestFeature |
| Restore | `dotnet restore CodeCompress.slnx` | Restore NuGet packages |
| Build | `dotnet build CodeCompress.slnx --no-restore -c Release` | `TreatWarningsAsErrors` ensures zero warnings = zero SonarAnalyzer violations |
| Test | `dotnet test CodeCompress.slnx --no-build -c Release --logger "trx;LogFileName=results.trx" --results-directory ./test-results` | TRX format for artifact upload |
| Upload test results | `actions/upload-artifact@v4` with `path: ./test-results/*.trx` | Available for download on failed runs |

### 3. Zero-Warning Enforcement

The solution's `Directory.Build.props` sets `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-all` with `SonarAnalyzer.CSharp`. This means the `dotnet build` step will fail if any analyzer warning is produced. No additional static analysis step is needed — it is built into compilation.

### 4. Optional Code Coverage

| Aspect | Detail |
|---|---|
| Collection | `dotnet test` with `--collect:"XPlat Code Coverage"` and `coverlet.collector` |
| Report | `reportgenerator` tool to produce HTML/Cobertura summary |
| Upload | Upload coverage report as artifact; optionally publish to Codecov or similar |
| Thresholds | Not enforced in CI for MVP — targets are documented in CLAUDE.md |

Coverage collection is a stretch goal — the workflow should work without it and can be added later.

### 5. Status Badge

Add a CI status badge to the repository's README (if one exists):

```markdown
![CI](https://github.com/{owner}/{repo}/actions/workflows/ci.yml/badge.svg)
```

### 6. Workflow Validation

The YAML file should be validated for correctness. Options:
- Use `actionlint` locally before committing.
- Manual review against GitHub Actions documentation.
- Push to a branch and verify the workflow runs.

## Acceptance Criteria

- [ ] `.github/workflows/ci.yml` exists and is valid GitHub Actions YAML.
- [ ] Workflow triggers on push to `main` and on pull requests targeting `main`.
- [ ] Matrix includes `ubuntu-latest`, `windows-latest`, and `macos-latest`.
- [ ] .NET 10 SDK is set up correctly.
- [ ] `dotnet restore`, `dotnet build`, and `dotnet test` steps execute in order.
- [ ] Build uses `-c Release` and `--no-restore` for efficiency.
- [ ] Test results are saved in TRX format and uploaded as artifacts.
- [ ] Build step fails on any compiler or analyzer warning (via existing `TreatWarningsAsErrors`).
- [ ] Workflow passes on a clean build with all tests green.
- [ ] `fail-fast: false` ensures all matrix entries run even if one fails.

## Files to Create/Modify

### Create

| File | Description |
|---|---|
| `.github/workflows/ci.yml` | GitHub Actions CI workflow definition |

### Modify

| File | Description |
|---|---|
| `README.md` | Add CI status badge (if README exists) |

## Out of Scope

- Release workflow (covered in 13-002).
- Deployment to any hosting environment — CodeCompress is a local tool.
- Docker image building — not needed for a dotnet tool.
- Branch protection rules — those are configured in GitHub settings, not in workflow files.
- Dependabot or Renovate configuration for dependency updates.
- Performance or benchmarking CI steps.

## Notes / Decisions

1. **Matrix strategy.** Running on all three major platforms ensures cross-platform compatibility. The `macos-latest` runner may be slower or have limited .NET 10 support initially — it can be removed from the matrix if it causes issues.
2. **No separate linting step.** SonarAnalyzer runs during `dotnet build` and `TreatWarningsAsErrors` enforces zero violations. This is simpler and faster than a separate analysis step.
3. **TRX test results.** The TRX format is natively supported by .NET and can be viewed in Visual Studio or converted to other formats. Uploading as artifacts allows debugging failed CI runs.
4. **No caching.** NuGet package caching via `actions/cache` could speed up CI but adds complexity. For MVP, the `dotnet restore` step runs fresh each time. Caching can be added if CI times become a problem.
5. **.NET 10 availability.** As of the project's target date, .NET 10 may still be in preview. The `setup-dotnet` action supports preview versions via `include-prerelease: true` if needed.
