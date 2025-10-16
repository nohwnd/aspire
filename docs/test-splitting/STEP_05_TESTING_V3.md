# Step 5: Testing & Validation Guide

## Overview

This guide provides step-by-step instructions for testing the implementation locally before pushing to CI.

## Prerequisites

- PowerShell 7.0+ installed
- .NET SDK matching `global.json`
- Aspire repository cloned locally

## Phase 1: Test PowerShell Scripts in Isolation

### Test 1: Discovery Helper Script

```powershell
# Create mock test output
$mockOutput = @(
    "Collection: DatabaseTests",
    "  Aspire.Hosting.Tests.PostgresTests.CanStartContainer",
    "  Aspire.Hosting.Tests.PostgresTests.CanConnect",
    "Collection: ContainerTests",
    "  Aspire.Hosting.Tests.DockerTests.CanStartContainer",
    "Aspire.Hosting.Tests.QuickTests.FastTest1"
)

# Test the script
pwsh eng/scripts/extract-test-metadata.ps1 `
    -TestAssemblyOutput $mockOutput `
    -TestClassNamesPrefix "Aspire.Hosting.Tests" `
    -OutputListFile "./test-output.list"
```

**Expected Output File**:
```
collection:ContainerTests
collection:DatabaseTests
uncollected:*
```

**Validation**:
- [ ] Script runs without errors
- [ ] Output file created
- [ ] Contains 3 lines (2 collections + uncollected)
- [ ] Collections are sorted alphabetically

### Test 2: Matrix Generator Script

```powershell
# Create test files
mkdir -p artifacts/helix

# Create .tests.list
@"
collection:DatabaseTests
collection:ContainerTests
uncollected:*
"@ | Out-File -FilePath artifacts/helix/TestProject.tests.list -Encoding UTF8

# Create .tests.metadata.json
@"
{
  "projectName": "TestProject",
  "testProjectPath": "tests/TestProject/TestProject.csproj",
  "mode": "collection",
  "collections": "DatabaseTests;ContainerTests",
  "testSessionTimeout": "20m",
  "testHangTimeout": "10m"
}
"@ | Out-File -FilePath artifacts/helix/TestProject.tests.metadata.json -Encoding UTF8

# Run generator
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected Output**:
- [ ] Matrix JSON file created
- [ ] Contains 3 entries (2 collections + 1 uncollected)
- [ ] Each entry has `type`, `name`, `shortname`, `filterArg`, etc.
- [ ] Filter args are correct:
  - `--filter-collection "DatabaseTests"`
  - `--filter-collection "ContainerTests"`
  - `--filter-not-collection "DatabaseTests" --filter-not-collection "ContainerTests"`

**Validate JSON**:
```powershell
# Check JSON is valid
$matrix = Get-Content ./artifacts/test-matrices/split-tests-matrix.json | ConvertFrom-Json
$matrix.include.Count  # Should be 3

# Or use jq
jq '.include | length' ./artifacts/test-matrices/split-tests-matrix.json
# Should output: 3
```

## Phase 2: Test MSBuild Integration

### Test 1: Build Test Project with Splitting Enabled

Choose a test project to experiment with (or create a dummy one):

```bash
# Build with splitting enabled
dotnet build tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsOnCI=true \
  -p:TestClassNamesPrefix=Aspire.Templates.Tests \
  -p:InstallBrowsersForPlaywright=false \
  /bl:build.binlog
```

**Expected Output**:
```
[Aspire.Templates.Tests] Starting test metadata extraction...
[Aspire.Templates.Tests] Running discovery helper...
ℹ️ Parsing test assembly output...
✅ Detection Results:
ℹ️   Mode: class  (or "collection" if you added [Collection] attributes)
...
[Aspire.Templates.Tests] ✅ Test metadata extraction complete!
```

**Validation**:
- [ ] Build succeeds
- [ ] Files created in `artifacts/helix/`:
  - [ ] `Aspire.Templates.Tests.tests.list`
  - [ ] `Aspire.Templates.Tests.tests.metadata.json`
- [ ] Binlog shows ExtractTestClassNames target executed
- [ ] No errors in console output

### Test 2: Verify Generated Files

```bash
# Check .tests.list
cat artifacts/helix/Aspire.Templates.Tests.tests.list

# Check metadata
cat artifacts/helix/Aspire.Templates.Tests.tests.metadata.json | jq .

# Verify mode
cat artifacts/helix/Aspire.Templates.Tests.tests.metadata.json | jq -r .mode
# Should output: "class" or "collection"
```

### Test 3: Generate Matrix

```bash
# Run the full GetTestProjects.proj
dotnet build tests/Shared/GetTestProjects.proj \
  /p:TestsListOutputPath=$PWD/artifacts/TestsForGithubActions.list \
  /p:TestMatrixOutputPath=$PWD/artifacts/test-matrices/ \
  /p:ContinuousIntegrationBuild=true \
  /bl:get-test-projects.binlog
```

**Validation**:
- [ ] `artifacts/TestsForGithubActions.list` created (regular tests)
- [ ] `artifacts/TestsForGithubActions.list.split-projects` created (split tests)
- [ ] `artifacts/test-matrices/split-tests-matrix.json` created
- [ ] Matrix JSON is valid

```bash
# Validate
jq . artifacts/test-matrices/split-tests-matrix.json
```

## Phase 3: Test with Real Project

### Option A: Test with Aspire.Templates.Tests (No Collections)

```bash
# 1. Update .csproj (already has splitting, just verify)
# 2. Build
./build.sh -restore -build -projects tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj

# 3. Extract metadata
dotnet build tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsOnCI=true \
  -p:InstallBrowsersForPlaywright=false

# 4. Check mode
cat artifacts/helix/Aspire.Templates.Tests.tests.metadata.json | jq -r .mode
# Expected: "class"

# 5. Count entries
cat artifacts/helix/Aspire.Templates.Tests.tests.list | wc -l
# Expected: ~12 (one per test class)
```

### Option B: Test with Aspire.Hosting.Tests (Add Collections)

```bash
# 1. Add [Collection] attributes to some test classes
# Edit: tests/Aspire.Hosting.Tests/SomeTests.cs

# 2. Enable splitting in .csproj
# Add:
#   <SplitTestsOnCI>true</SplitTestsOnCI>
#   <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>

# 3. Build
./build.sh -restore -build -projects tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj

# 4. Extract metadata
dotnet build tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsOnCI=true

# 5. Check mode
cat artifacts/helix/Aspire.Hosting.Tests.tests.metadata.json | jq -r .mode
# Expected: "collection"

# 6. Check collections
cat artifacts/helix/Aspire.Hosting.Tests.tests.list
# Expected:
#   collection:YourCollectionName
#   collection:AnotherCollection
#   uncollected:*
```

## Phase 4: Test Full Workflow Locally

### Simulate GitHub Actions Enumerate Step

```bash
# Run the enumerate-tests action logic locally
dotnet build tests/Shared/GetTestProjects.proj \
  /p:TestsListOutputPath=$PWD/artifacts/TestsForGithubActions.list \
  /p:TestMatrixOutputPath=$PWD/artifacts/test-matrices/ \
  /p:ContinuousIntegrationBuild=true

# Check split projects
cat artifacts/TestsForGithubActions.list.split-projects
# Should list: Templates or Hosting (whichever has SplitTestsOnCI=true)

# Build each split project
while read project; do
  echo "Building $project..."
  dotnet build tests/Aspire.$project.Tests/Aspire.$project.Tests.csproj \
    /t:Build;ExtractTestClassNames \
    -p:PrepareForHelix=true \
    -p:SplitTestsOnCI=true \
    -p:InstallBrowsersForPlaywright=false
done < artifacts/TestsForGithubActions.list.split-projects

# Generate matrix
pwsh eng/scripts/generate-test-matrix.ps1 \
  -TestListsDirectory ./artifacts/helix \
  -OutputDirectory ./artifacts/test-matrices \
  -BuildOs linux

# Verify matrix
jq '.include[] | {shortname, filterArg}' artifacts/test-matrices/split-tests-matrix.json
```

## Phase 5: Verify Filter Arguments Work

### Test Collection Filter

```bash
# Run tests with collection filter
dotnet test artifacts/bin/Aspire.Hosting.Tests/Debug/net9.0/Aspire.Hosting.Tests.dll \
  -- --filter-collection "DatabaseTests"

# Should only run tests in DatabaseTests collection
```

### Test Class Filter

```bash
# Run tests with class filter
dotnet test artifacts/bin/Aspire.Templates.Tests/Debug/net9.0/Aspire.Templates.Tests.dll \
  -- --filter-class "Aspire.Templates.Tests.BuildAndRunTemplateTests"

# Should only run tests in that class
```

### Test Uncollected Filter

```bash
# Run tests NOT in collections
dotnet test artifacts/bin/Aspire.Hosting.Tests/Debug/net9.0/Aspire.Hosting.Tests.dll \
  -- --filter-not-collection "DatabaseTests" --filter-not-collection "ContainerTests"

# Should only run tests without [Collection] attributes
```

## Validation Checklist

### PowerShell Scripts
- [ ] `extract-test-metadata.ps1` runs without errors
- [ ] `extract-test-metadata.ps1` detects collections correctly
- [ ] `extract-test-metadata.ps1` falls back to class mode when no collections
- [ ] `generate-test-matrix.ps1` creates valid JSON
- [ ] `generate-test-matrix.ps1` handles both collection and class modes

### MSBuild Integration
- [ ] ExtractTestClassNames target executes
- [ ] `.tests.list` file is generated
- [ ] `.tests.metadata.json` file is generated
- [ ] Mode is correctly detected and stored in metadata
- [ ] GetTestProjects.proj identifies split projects

### Generated Artifacts
- [ ] `.tests.list` format is correct
- [ ] `.tests.metadata.json` is valid JSON
- [ ] `split-tests-matrix.json` is valid JSON
- [ ] All matrix entries have required fields
- [ ] Filter arguments have correct syntax

### xUnit Filters
- [ ] `--filter-collection` works
- [ ] `--filter-class` works
- [ ] `--filter-not-collection` works
- [ ] Filters run expected number of tests

## Troubleshooting

### Issue: "PowerShell script not found"

**Error**: `Cannot find path 'eng/scripts/extract-test-metadata.ps1'`

**Fix**: Ensure working directory is repository root:
```bash
cd /path/to/aspire
pwd  # Should show aspire repo root
```

### Issue: "No tests found matching prefix"

**Error**: `Error: No test classes found matching prefix`

**Fix**: Verify `TestClassNamesPrefix` matches actual test namespace:
```bash
# Check test namespace
grep -r "^namespace " tests/YourProject.Tests/*.cs | head -1
# Should match TestClassNamesPrefix
```

### Issue: "Mode is empty in metadata"

**Error**: Mode field is empty or missing

**Fix**: Check PowerShell script output - may have parsing errors.
Look in binlog for script console output.

### Issue: "Matrix JSON is invalid"

**Error**: GitHub Actions can't parse matrix

**Fix**: Validate JSON locally:
```bash
jq empty artifacts/test-matrices/split-tests-matrix.json
# Exit code 0 = valid, non-zero = invalid
```

## Next Steps

Once local testing passes:
1. Create PR with changes
2. Push to branch
3. Monitor GitHub Actions workflow
4. Verify matrices are generated correctly
5. Verify tests run in split jobs
6. Compare CI times before/after