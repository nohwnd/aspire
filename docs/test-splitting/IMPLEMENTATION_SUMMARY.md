# Test Splitting Implementation - Summary & Checklist

**Date**: 2025-01-16  
**Author**: @radical  
**Status**: Ready for Implementation

## Overview

This implementation adds automatic test splitting to dotnet/aspire CI, reducing test execution time by running tests in parallel.

**Key Innovation**: Auto-detection of splitting strategy
- Has `[Collection]` attributes? → Split by collection + uncollected
- No collections? → Split by test class
- Not enabled? → Run as single job (no change)

## What's Being Implemented

### New Files

1. **`eng/scripts/extract-test-metadata.ps1`** (Step 1)
   - Parses `--list-tests` output
   - Detects collections vs classes
   - Outputs `.tests.list` file

2. **`eng/scripts/generate-test-matrix.ps1`** (Step 3)
   - Reads `.tests.list` and `.tests.metadata.json`
   - Generates JSON matrix for CI
   - Handles both collection and class modes

### Modified Files

3. **`tests/Directory.Build.targets`** (Step 2)
   - Enhanced `ExtractTestClassNames` target
   - Calls PowerShell discovery helper
   - Writes metadata for matrix generation

4. **`tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj`** (Step 4)
   - Migrate from old custom mechanism
   - Use new unified `SplitTestsOnCI` property

### Existing Files (No Changes)

- `.github/workflows/tests.yml` - Already supports new matrix format
- `.github/actions/enumerate-tests/action.yml` - Already calls scripts correctly
- `tests/Shared/GetTestProjects.proj` - Already orchestrates correctly

## Implementation Checklist

### Phase 1: Infrastructure (Week 1)

- [ ] **Create `eng/scripts/extract-test-metadata.ps1`**
  - [ ] Copy from STEP_01_DISCOVERY_HELPER.md
  - [ ] Test with mock data (see Step 5)
  - [ ] Verify collections detected correctly
  - [ ] Verify class-only mode works

- [ ] **Create `eng/scripts/generate-test-matrix.ps1`**
  - [ ] Copy from STEP_03_MATRIX_GENERATOR_V3.md
  - [ ] Test with sample .tests.list files (see Step 5)
  - [ ] Verify JSON output is valid
  - [ ] Test both collection and class modes

- [ ] **Update `tests/Directory.Build.targets`**
  - [ ] Add enhanced ExtractTestClassNames target from STEP_02_MSBUILD_TARGETS_V3.md
  - [ ] Test locally with `dotnet build` (see Step 5)
  - [ ] Verify `.tests.list` and `.tests.metadata.json` are created
  - [ ] Check binlog for errors

### Phase 2: Migrate Templates.Tests (Week 2)

- [ ] **Update `tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj`**
  - [ ] Replace `ExtractTestClassNamesForHelix` with `SplitTestsOnCI`
  - [ ] Add `RequiresNugetsForSplitTests=true`
  - [ ] Add `RequiresTestSdkForSplitTests=true`
  - [ ] Add `EnablePlaywrightInstallForSplitTests=true`
  - [ ] Remove `TestArchiveTestsDir` override

- [ ] **Test Locally**
  - [ ] Build project with splitting enabled
  - [ ] Verify class-based mode detected (no collections in templates tests)
  - [ ] Check `.tests.list` has `class:` entries
  - [ ] Verify matrix has same number of jobs as before

- [ ] **Create PR**
  - [ ] Title: "Migrate Aspire.Templates.Tests to unified test splitting"
  - [ ] Link to this implementation plan
  - [ ] Test in CI
  - [ ] Verify same behavior as before

### Phase 3: Enable Hosting.Tests (Week 3)

- [ ] **Add Collections to Slow Tests**
  - [ ] Identify slow test groups (>10 min combined)
  - [ ] Add `[Collection("DatabaseTests")]` to database test classes
  - [ ] Add `[Collection("ContainerTests")]` to container test classes
  - [ ] Leave fast tests without `[Collection]` attribute

- [ ] **Update `tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj`**
  - [ ] Add `SplitTestsOnCI=true`
  - [ ] Add `TestClassNamesPrefix=Aspire.Hosting.Tests`
  - [ ] Set timeouts (see Step 4)

- [ ] **Test Locally**
  - [ ] Build with splitting enabled
  - [ ] Verify collection-based mode detected
  - [ ] Check `.tests.list` has `collection:` entries
  - [ ] Test filters work (see Step 5)

- [ ] **Create PR**
  - [ ] Title: "Enable test splitting for Aspire.Hosting.Tests"
  - [ ] Document expected CI time improvement
  - [ ] Monitor CI times after merge

### Phase 4: Rollout & Optimize (Week 4)

- [ ] **Identify Other Long-Running Projects**
  - [ ] Review CI times for all test projects
  - [ ] List projects > 15 minutes
  - [ ] Prioritize by impact

- [ ] **Enable Splitting Incrementally**
  - [ ] One project per PR
  - [ ] Monitor each for issues
  - [ ] Adjust collection groupings as needed

- [ ] **Document Best Practices**
  - [ ] Collection size guidelines
  - [ ] When to split vs not split
  - [ ] Troubleshooting common issues

## Testing Strategy

### Local Testing (Before Each PR)

1. **Unit Test Scripts**
   - [ ] Test `extract-test-metadata.ps1` with mock data
   - [ ] Test `generate-test-matrix.ps1` with sample files
   - [ ] Verify JSON output structure

2. **Integration Test MSBuild**
   - [ ] Build test project with splitting enabled
   - [ ] Verify files generated in `artifacts/helix/`
   - [ ] Check mode detection is correct

3. **End-to-End Test**
   - [ ] Run full `GetTestProjects.proj`
   - [ ] Generate matrix JSON
   - [ ] Validate matrix structure
   - [ ] Test xUnit filters work

### CI Testing (After Push)

1. **Setup Jobs**
   - [ ] All 3 OS setup jobs succeed
   - [ ] Matrices are generated
   - [ ] Artifacts are uploaded

2. **Split Test Jobs**
   - [ ] New jobs appear as expected
   - [ ] Tests run with correct filters
   - [ ] Results are uploaded
   - [ ] No unexpected failures

3. **Performance**
   - [ ] CI times reduced as expected
   - [ ] No increase in flakiness
   - [ ] Resource usage acceptable

## Success Criteria

### Functional

- [ ] Auto-detection works (collection vs class mode)
- [ ] Templates.Tests migrates without behavior change
- [ ] Hosting.Tests splits into ~3-5 jobs
- [ ] All tests pass in split jobs
- [ ] Test results are properly reported
- [ ] Works on all 3 OSes (Linux, macOS, Windows)

### Performance

- [ ] Hosting.Tests CI time reduced by 50%+
- [ ] No increase in test flakiness
- [ ] Job count remains manageable (<10 per project per OS)

### Maintainability

- [ ] Clear documentation for developers
- [ ] Easy to enable for new projects
- [ ] Easy to troubleshoot issues
- [ ] No breaking changes to existing projects

## Rollback Plan

If critical issues arise:

### Per-Project Rollback

```xml
<!-- In problematic .csproj, remove or comment out: -->
<!-- <SplitTestsOnCI>true</SplitTestsOnCI> -->
```

Project reverts to single-job execution immediately.

### Full Rollback

Revert the PR that modified `Directory.Build.targets`.  
All projects revert to original behavior.

## File Reference

| Step | File(s) | Purpose |
|------|---------|---------|
| 1 | `STEP_01_DISCOVERY_HELPER.md` | PowerShell script to detect collections/classes |
| 2 | `STEP_02_MSBUILD_TARGETS_V3.md` | MSBuild target that calls discovery helper |
| 3 | `STEP_03_MATRIX_GENERATOR_V3.md` | PowerShell script to generate JSON matrices |
| 4 | `STEP_04_PROJECT_CONFIG_V3.md` | How to configure test projects |
| 5 | `STEP_05_TESTING_V3.md` | Local testing guide |
| 6 | `STEP_06_CI_INTEGRATION.md` | CI verification guide |

## Questions for Copilot

Before starting implementation, Copilot should clarify:

1. **Templates.Tests Migration**: Should we remove the old `enumerate-tests` template-specific logic in the workflow, or keep it as fallback?

2. **Timeout Defaults**: What should default timeout values be if not specified?
   - Suggested: `SplitTestSessionTimeout=20m`, `UncollectedTestsSessionTimeout=15m`

3. **Collection Naming**: Any conventions or restrictions on collection names?
   - Suggested: Alphanumeric + underscore only

4. **Error Handling**: Should we fail CI if splitting is enabled but no tests found, or fall back to running all tests?
   - Suggested: Fail fast to catch configuration errors early

5. **Artifacts**: Should we always upload `.tests.list` and `.tests.metadata.json` files, even on success?
   - Suggested: Yes, for debugging and transparency

## Ready for Implementation?

- [x] All design documents complete
- [x] Testing strategy defined
- [x] Success criteria clear
- [x] Rollback plan in place
- [x] Questions for Copilot identified

**Status**: ✅ Ready to hand off to Copilot for PR creation

**Estimated Implementation Time**: 2-3 hours for infrastructure + testing

**Recommended Approach**: Implement in 3 separate PRs:
1. PR #1: Add infrastructure (scripts + targets) - test with Templates.Tests
2. PR #2: Enable Hosting.Tests with collections
3. PR #3: Roll out to remaining long-running projects