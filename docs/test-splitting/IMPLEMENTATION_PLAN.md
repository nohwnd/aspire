# Test Splitting Implementation Plan for dotnet/aspire

**Date**: 2025-10-16  
**Author**: @radical  
**Objective**: Implement a unified, MSBuild-based test splitting mechanism that works across all 3 OSes (Linux, macOS, Windows) and both CI systems (GitHub Actions, Azure DevOps).

## Overview

This plan implements automatic test partitioning by class for long-running test projects. The mechanism:
- ✅ Works on all 3 OSes (Linux, macOS, Windows)
- ✅ Works on GitHub Actions and Azure DevOps
- ✅ Uses MSBuild + PowerShell for deterministic, version-controlled matrix generation
- ✅ Allows simple opt-in via project properties
- ✅ Maintains backward compatibility with existing non-split tests

## Current State

### Existing Split Tests
- **Aspire.Templates.Tests**: Already uses class-based splitting
- Splits into ~10-15 test classes
- Each OS generates its own matrix (separate setup jobs)
- Uses `--filter-class` to run individual classes

### Problem Statement
3-4 test projects have very long run times:
1. **Aspire.Hosting.Tests** - Very long, needs splitting
2. Likely other Hosting-related tests
3. Some integration test projects

Currently only Templates.Tests uses splitting. We need a **common mechanism** that:
- Any test project can opt into
- Automatically handles class enumeration
- Generates appropriate matrices for all OSes
- Requires minimal YAML changes

## Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        GitHub Actions                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐      │
│  │ setup_for_    │  │ setup_for_    │  │ setup_for_    │      │
│  │ tests_lin     │  │ tests_macos   │  │ tests_win     │      │
│  │ (ubuntu)      │  │ (macos)       │  │ (windows)     │      │
│  └───────┬───────┘  └───────┬───────┘  └───────┬───────┘      │
│          │                  │                  │                │
│          └─────────┬────────┴────────┬─────────┘                │
│                    ▼                 ▼                          │
│            ┌───────────────────────────────────┐                │
│            │  .github/actions/enumerate-tests  │                │
│            └───────────────┬───────────────────┘                │
│                            ▼                                    │
│            ┌───────────────────────────────────┐                │
│            │ tests/Shared/GetTestProjects.proj │                │
│            │  (MSBuild orchestration)          │                │
│            └───────────────┬───────────────────┘                │
│                            │                                    │
│         ┌──────────────────┼──────────────────┐                 │
│         ▼                  ▼                  ▼                 │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐          │
│  │ Regular     │  │ Build Split  │  │ Generate      │          │
│  │ Tests List  │  │ Test Projects│  │ Matrices      │          │
│  └─────────────┘  └──────┬───────┘  └───────┬───────┘          │
│                          │                  │                  │
│                          ▼                  ▼                  │
│              ┌────────────────────────────────────┐            │
│              │ eng/scripts/generate-test-matrix.ps1│            │
│              │  (PowerShell - reads .tests.list   │            │
│              │   and .metadata.json files)        │            │
│              └────────────────┬───────────────────┘            │
│                               ▼                                │
│              ┌────────────────────────────────┐                │
│              │ artifacts/test-matrices/       │                │
│              │   split-tests-matrix.json      │                │
│              └────────────────────────────────┘                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
Per-OS Setup Job
    ↓
enumerate-tests action
    ↓
GetTestProjects.proj (MSBuild)
    ↓
    ├─→ Regular Tests → .list file
    │
    └─→ Split Tests Projects → .list.split-projects
            ↓
        Build each split project
            ↓
        ExtractTestClassNames target
            ↓
        Generate per-project:
            ├─→ ProjectName.tests.list (test class names)
            └─→ ProjectName.tests.metadata.json (config)
                    ↓
                generate-test-matrix.ps1
                    ↓
                split-tests-matrix.json
                    ↓
                GitHub Actions matrix
```

## Implementation Steps

See individual files:
1. [Step 1: MSBuild Targets](./STEP_01_MSBUILD_TARGETS.md)
2. [Step 2: PowerShell Script](./STEP_02_POWERSHELL_SCRIPT.md)
3. [Step 3: GitHub Actions](./STEP_03_GITHUB_ACTIONS.md)
4. [Step 4: Project Configuration](./STEP_04_PROJECT_CONFIG.md)
5. [Step 5: Testing & Validation](./STEP_05_TESTING.md)

## OS-Specific Considerations

### Per-OS Matrix Generation

Each OS generates its own matrix in parallel:
- **Linux** (ubuntu-latest): `setup_for_tests_lin`
- **macOS** (macos-latest): `setup_for_tests_macos`
- **Windows** (windows-latest): `setup_for_tests_win`

This is critical because:
1. Projects can opt-in/out per OS via `RunOnGithubActions{Windows|Linux|MacOS}` properties
2. File paths differ (slash direction)
3. Some tests only run on specific OSes (e.g., Docker on Linux)

### PowerShell Cross-Platform

The `generate-test-matrix.ps1` script:
- ✅ Uses PowerShell Core features (cross-platform)
- ✅ Uses `System.IO.Path.Combine()` for path handling
- ✅ Avoids OS-specific cmdlets
- ✅ Tested on all 3 OSes

## Migration Strategy

### Phase 1: Infrastructure (Week 1)
- Implement MSBuild targets
- Create PowerShell script
- Update enumerate-tests action
- Test with Aspire.Templates.Tests (already splitting)

### Phase 2: Enable for Long-Running Tests (Week 2)
- Migrate Aspire.Templates.Tests to new mechanism
- Enable splitting for Aspire.Hosting.Tests
- Enable for 2-3 other long-running projects
- Monitor CI times

### Phase 3: Optimization (Week 3)
- Analyze job distribution
- Fine-tune timeouts
- Add any missing metadata fields
- Document usage

## Success Criteria

- ✅ All OSes generate correct matrices
- ✅ Split tests run in parallel per class
- ✅ Regular tests continue to work unchanged
- ✅ CI time for long-running projects reduced by 50%+
- ✅ No increase in flakiness
- ✅ Works on both GitHub Actions and Azure DevOps

## Rollback Plan

If issues arise:
1. Set `SplitTestsForCI=false` in problematic project
2. Project reverts to regular single-job execution
3. No YAML changes needed (matrix will be empty)

## Files Modified/Created

### New Files
- `eng/scripts/generate-test-matrix.ps1`
- `docs/testing/test-splitting.md` (documentation)

### Modified Files
- `tests/Directory.Build.targets`
- `tests/Shared/GetTestProjects.proj`
- `.github/actions/enumerate-tests/action.yml`
- `.github/workflows/tests.yml`
- `tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj`
- `tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj` (if enabled)

## Next Steps

1. Review this plan
2. Begin implementation following step-by-step guides
3. Create PR with Phase 1 changes
4. Test thoroughly on all OSes
5. Gradually roll out to long-running projects

---

**Implementation Details**: See individual step markdown files in this directory.