# Step 3: GitHub Actions Integration

## Overview

Update GitHub Actions workflows to use the new MSBuild-based matrix generation while maintaining full support for all 3 OSes.

## Critical Requirement: Per-OS Matrix Generation

**Each OS MUST generate its own matrix** because:
1. Projects can opt-in/out per OS (`RunOnGithubActionsWindows`, etc.)
2. Some tests only run on specific OSes (e.g., Docker tests on Linux)
3. File path differences between OSes
4. Test discovery may differ per platform

## File: `.github/actions/enumerate-tests/action.yml`

### Complete Replacement

```yaml
name: 'Enumerate test projects'
description: 'Enumerate test projects and generate test matrices for the current OS'
inputs:
  includeIntegrations:
    description: 'Include integration tests in enumeration'
    required: false
    type: boolean
    default: false
  includeSplitTests:
    description: 'Include and generate split test matrices'
    required: false
    type: boolean
    default: false

outputs:
  integrations_tests_matrix:
    description: 'JSON matrix of integration test projects'
    value: ${{ steps.load_integrations_matrix.outputs.matrix }}
  split_tests_matrix:
    description: 'JSON matrix of split test classes'
    value: ${{ steps.load_split_matrix.outputs.matrix }}
    
runs:
  using: "composite"
  steps:
    - name: Checkout code
      uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2

    - name: Set up .NET Core
      uses: actions/setup-dotnet@3951f0dfe7a07e2313ec93c75700083e2005cbab # v4.3.0
      with:
        global-json-file: ${{ github.workspace }}/global.json

    - name: Generate test project lists
      if: ${{ inputs.includeIntegrations }}
      shell: pwsh
      run: >
        dotnet build ${{ github.workspace }}/tests/Shared/GetTestProjects.proj
        /bl:${{ github.workspace }}/artifacts/log/Debug/GetTestProjects.binlog
        /p:TestsListOutputPath=${{ github.workspace }}/artifacts/TestsForGithubActions.list
        /p:TestMatrixOutputPath=${{ github.workspace }}/artifacts/test-matrices/
        /p:ContinuousIntegrationBuild=true

    - name: Build split test projects
      if: ${{ inputs.includeSplitTests }}
      shell: pwsh
      run: |
        $ErrorActionPreference = 'Stop'
        
        $splitProjectsFile = "${{ github.workspace }}/artifacts/TestsForGithubActions.list.split-projects"
        
        if (-not (Test-Path $splitProjectsFile)) {
          Write-Host "::notice::No split test projects found for ${{ runner.os }}"
          exit 0
        }
        
        $splitProjects = Get-Content $splitProjectsFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        
        if ($splitProjects.Count -eq 0) {
          Write-Host "::notice::No split test projects to build for ${{ runner.os }}"
          exit 0
        }
        
        Write-Host "::group::Building $($splitProjects.Count) split test project(s) for ${{ runner.os }}"
        
        foreach ($shortname in $splitProjects) {
          Write-Host "Processing $shortname..."
          
          # Find the project file (try both naming patterns)
          $projectPath1 = "${{ github.workspace }}/tests/$shortname.Tests/$shortname.Tests.csproj"
          $projectPath2 = "${{ github.workspace }}/tests/Aspire.$shortname.Tests/Aspire.$shortname.Tests.csproj"
          
          if (Test-Path $projectPath1) {
            $projectPath = $projectPath1
          } elseif (Test-Path $projectPath2) {
            $projectPath = $projectPath2
          } else {
            Write-Error "::error::Could not find project for $shortname"
            exit 1
          }
          
          Write-Host "  Building: $projectPath"
          
          # Build with ExtractTestClassNames target
          dotnet build $projectPath `
            /t:Build`;ExtractTestClassNames `
            /bl:${{ github.workspace }}/artifacts/log/Debug/Build_$shortname.binlog `
            -p:PrepareForHelix=true `
            -p:SplitTestsForCI=true `
            -p:InstallBrowsersForPlaywright=false
          
          if ($LASTEXITCODE -ne 0) {
            Write-Error "::error::Build failed for $shortname with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
          }
          
          Write-Host "  ✅ Successfully built $shortname"
        }
        
        Write-Host "::endgroup::"
        Write-Host "::notice::Successfully built all $($splitProjects.Count) split test projects for ${{ runner.os }}"

    - name: Load integrations matrix
      id: load_integrations_matrix
      if: ${{ inputs.includeIntegrations }}
      shell: pwsh
      run: |
        $filePath = "${{ github.workspace }}/artifacts/TestsForGithubActions.list"
        
        if (-not (Test-Path $filePath)) {
          Write-Error "::error::Test list file not found: $filePath"
          exit 1
        }
        
        $lines = Get-Content $filePath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        
        $matrix = @{ 
          shortname = $lines | Sort-Object 
        }
        
        $json = $matrix | ConvertTo-Json -Compress
        
        Write-Host "::notice::Generated integrations matrix for ${{ runner.os }} with $($lines.Count) project(s)"
        "matrix=$json" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append

    - name: Load split tests matrix
      id: load_split_matrix
      if: ${{ inputs.includeSplitTests }}
      shell: pwsh
      run: |
        $matrixFile = "${{ github.workspace }}/artifacts/test-matrices/split-tests-matrix.json"
        
        if (Test-Path $matrixFile) {
          $json = Get-Content $matrixFile -Raw
          $matrix = $json | ConvertFrom-Json
          
          $testCount = if ($matrix.include) { $matrix.include.Count } else { 0 }
          
          Write-Host "::notice::Generated split tests matrix for ${{ runner.os }} with $testCount test(s)"
          "matrix=$json" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
        } else {
          Write-Host "::notice::No split tests matrix found for ${{ runner.os }}, using empty matrix"
          $emptyMatrix = @{ include = @() } | ConvertTo-Json -Compress
          "matrix=$emptyMatrix" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
        }

    - name: Upload artifacts
      if: always()
      uses: actions/upload-artifact@4cec3d8aa04e39d1a68397de0c4cd6fb9dce8ec1 # v4.6.1
      with:
        name: logs-enumerate-tests-${{ runner.os }}
        path: |
          artifacts/log/**/*.binlog
          artifacts/**/*.list
          artifacts/**/*.metadata.json
          artifacts/test-matrices/**/*.json
        if-no-files-found: warn
```

## File: `.github/workflows/tests.yml`

### Modified Sections

#### 1. Update setup jobs (KEEP SEPARATE PER OS)

```yaml
jobs:
  # IMPORTANT: Keep separate setup jobs for each OS
  # Each OS generates its own matrix because projects can opt-in/out per OS
  
  setup_for_tests_lin:
    name: Setup for tests (Linux)
    runs-on: ubuntu-latest
    outputs:
      integrations_tests_matrix: ${{ steps.generate_tests_matrix.outputs.integrations_tests_matrix }}
      split_tests_matrix: ${{ steps.generate_tests_matrix.outputs.split_tests_matrix }}
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2

      - uses: ./.github/actions/enumerate-tests
        id: generate_tests_matrix
        with:
          includeIntegrations: true
          includeSplitTests: true  # NEW: Enable split tests

  setup_for_tests_macos:
    name: Setup for tests (macOS)
    runs-on: macos-latest
    outputs:
      integrations_tests_matrix: ${{ steps.generate_tests_matrix.outputs.integrations_tests_matrix }}
      split_tests_matrix: ${{ steps.generate_tests_matrix.outputs.split_tests_matrix }}
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2

      - uses: ./.github/actions/enumerate-tests
        id: generate_tests_matrix
        with:
          includeIntegrations: true
          includeSplitTests: true  # NEW: Enable split tests

  setup_for_tests_win:
    name: Setup for tests (Windows)
    runs-on: windows-latest
    outputs:
      integrations_tests_matrix: ${{ steps.generate_tests_matrix.outputs.integrations_tests_matrix }}
      split_tests_matrix: ${{ steps.generate_tests_matrix.outputs.split_tests_matrix }}
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2

      - uses: ./.github/actions/enumerate-tests
        id: generate_tests_matrix
        with:
          includeIntegrations: true
          includeSplitTests: true  # NEW: Enable split tests
```

#### 2. Add split test jobs (NEW)

```yaml
  # NEW: Split tests for Linux
  split_tests_lin:
    uses: ./.github/workflows/run-tests.yml
    name: Split Tests Linux
    needs: [setup_for_tests_lin, build_packages]
    if: ${{ fromJson(needs.setup_for_tests_lin.outputs.split_tests_matrix).include[0] != null }}
    strategy:
      fail-fast: false
      matrix: ${{ fromJson(needs.setup_for_tests_lin.outputs.split_tests_matrix) }}
    with:
      testShortName: "${{ matrix.projectName }}_${{ matrix.shortname }}"
      testProjectPath: "${{ matrix.testProjectPath }}"
      os: "ubuntu-latest"
      testSessionTimeout: "${{ matrix.testSessionTimeout }}"
      testHangTimeout: "${{ matrix.testHangTimeout }}"
      extraTestArgs: "--filter-not-trait quarantined=true --filter-not-trait outerloop=true --filter-class ${{ matrix.fullClassName }}"
      requiresNugets: ${{ matrix.requiresNugets }}
      requiresTestSdk: ${{ matrix.requiresTestSdk }}
      enablePlaywrightInstall: ${{ matrix.enablePlaywrightInstall }}
      versionOverrideArg: ${{ inputs.versionOverrideArg }}

  # NEW: Split tests for macOS
  split_tests_macos:
    uses: ./.github/workflows/run-tests.yml
    name: Split Tests macOS
    needs: [setup_for_tests_macos, build_packages]
    if: ${{ fromJson(needs.setup_for_tests_macos.outputs.split_tests_matrix).include[0] != null }}
    strategy:
      fail-fast: false
      matrix: ${{ fromJson(needs.setup_for_tests_macos.outputs.split_tests_matrix) }}
    with:
      testShortName: "${{ matrix.projectName }}_${{ matrix.shortname }}"
      testProjectPath: "${{ matrix.testProjectPath }}"
      os: "macos-latest"
      testSessionTimeout: "${{ matrix.testSessionTimeout }}"
      testHangTimeout: "${{ matrix.testHangTimeout }}"
      extraTestArgs: "--filter-not-trait quarantined=true --filter-not-trait outerloop=true --filter-class ${{ matrix.fullClassName }}"
      requiresNugets: ${{ matrix.requiresNugets }}
      requiresTestSdk: ${{ matrix.requiresTestSdk }}
      enablePlaywrightInstall: ${{ matrix.enablePlaywrightInstall }}
      versionOverrideArg: ${{ inputs.versionOverrideArg }}

  # NEW: Split tests for Windows
  split_tests_win:
    uses: ./.github/workflows/run-tests.yml
    name: Split Tests Windows
    needs: [setup_for_tests_win, build_packages]
    if: ${{ fromJson(needs.setup_for_tests_win.outputs.split_tests_matrix).include[0] != null }}
    strategy:
      fail-fast: false
      matrix: ${{ fromJson(needs.setup_for_tests_win.outputs.split_tests_matrix) }}
    with:
      testShortName: "${{ matrix.projectName }}_${{ matrix.shortname }}"
      testProjectPath: "${{ matrix.testProjectPath }}"
      os: "windows-latest"
      testSessionTimeout: "${{ matrix.testSessionTimeout }}"
      testHangTimeout: "${{ matrix.testHangTimeout }}"
      extraTestArgs: "--filter-not-trait quarantined=true --filter-not-trait outerloop=true --filter-class ${{ matrix.fullClassName }}"
      requiresNugets: ${{ matrix.requiresNugets }}
      requiresTestSdk: ${{ matrix.requiresTestSdk }}
      enablePlaywrightInstall: ${{ matrix.enablePlaywrightInstall }}
      versionOverrideArg: ${{ inputs.versionOverrideArg }}
```

#### 3. REMOVE old templates_test_* jobs

```yaml
# DELETE THESE (they'll use the new split_tests_* jobs instead):
# - templates_test_lin
# - templates_test_macos
# - templates_test_win
```

#### 4. Update results job dependencies

```yaml
  results:
    if: ${{ always() && github.repository_owner == 'dotnet' }}
    runs-on: ubuntu-latest
    name: Final Test Results
    needs: [
      endtoend_tests,
      extension_tests_win,
      integrations_test_lin,
      integrations_test_macos,
      integrations_test_win,
      split_tests_lin,      # NEW
      split_tests_macos,    # NEW
      split_tests_win       # NEW
    ]
    # ... rest of job unchanged ...
```

## Testing the Workflow Changes

### Test 1: Dry Run with Empty Matrix

Before enabling any split tests, verify the workflow handles empty matrices:

1. Don't set `SplitTestsForCI=true` in any project
2. Push to a branch
3. Verify workflow runs successfully
4. Check that split_tests_* jobs are skipped (due to `if` condition)

### Test 2: Enable for One Project

1. Enable splitting for Aspire.Templates.Tests (already configured)
2. Push to a branch
3. Verify:
   - 3 setup jobs run (one per OS)
   - Each generates a matrix
   - Split test jobs run in parallel
   - Each test class runs separately

### Test 3: Verify OS-Specific Matrices

Check that each OS can have different matrices:

1. Set a project to `RunOnGithubActionsLinux=true` but `RunOnGithubActionsWindows=false`
2. Verify Linux matrix includes it, Windows matrix doesn't
3. Verify Windows split_tests_win job is skipped or has fewer tests

## Important Notes

### Why Per-OS Setup Jobs?

```yaml
# ❌ DON'T DO THIS - Single setup job
setup_for_tests:
  runs-on: ubuntu-latest  # Only Linux!
  # This would only detect Linux tests
  
# ✅ DO THIS - Per-OS setup jobs  
setup_for_tests_lin:
  runs-on: ubuntu-latest
  
setup_for_tests_macos:
  runs-on: macos-latest
  
setup_for_tests_win:
  runs-on: windows-latest
```

### Matrix Conditional

The `if` condition prevents job failure when matrix is empty:

```yaml
if: ${{ fromJson(needs.setup_for_tests_lin.outputs.split_tests_matrix).include[0] != null }}
```

This checks if the matrix has at least one entry.

## Common Issues

### Issue: "Invalid matrix"

**Symptom**: Workflow fails with matrix parsing error  
**Cause**: Malformed JSON from PowerShell script  
**Fix**: Check `artifacts/test-matrices/split-tests-matrix.json` structure

### Issue: Split tests not running

**Symptom**: split_tests_* jobs are skipped  
**Cause**: Empty matrix or missing `includeSplitTests: true`  
**Fix**: Verify enumerate-tests action has correct inputs

### Issue: Tests run on wrong OS

**Symptom**: Linux tests running on Windows  
**Cause**: Using wrong matrix output  
**Fix**: Ensure each job uses the correct `needs.setup_for_tests_{os}.outputs`

## Next Steps

Proceed to [Step 4: Project Configuration](./STEP_04_PROJECT_CONFIG.md)