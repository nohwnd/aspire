# Test Splitting Implementation Plan v3 - Auto-Detection

**Date**: 2025-10-16  
**Author**: @radical  
**User**: radical  
**Objective**: Implement automatic detection of splitting strategy:
- Collections present → Split by collection + uncollected
- No collections → Split by class (original behavior)
- No `SplitTestsOnCI` → No splitting (run as single job)

## Overview

This v3 plan simplifies configuration by automatically detecting the appropriate splitting strategy.

## Auto-Detection Logic

```
Is SplitTestsOnCI=true?
  │
  ├─ NO → Run as single job (no splitting)
  │
  └─ YES → Build project and extract test metadata
            │
            ├─ Has Collections? → Split by Collection + Uncollected
            │   Result: N+1 jobs (one per collection + one uncollected)
            │
            └─ No Collections? → Split by Class
                Result: N jobs (one per test class)
```

## Splitting Modes

### Mode 1: No Splitting (Default)

```xml
<PropertyGroup>
  <!-- SplitTestsOnCI not set or false -->
</PropertyGroup>
```

**Result**: 1 job running entire test project

### Mode 2: Collection-Based Splitting (Auto-Detected)

```xml
<PropertyGroup>
  <SplitTestsOnCI>true</SplitTestsOnCI>
  <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
</PropertyGroup>
```

```csharp
[Collection("DatabaseTests")]
public class PostgresTests { }

[Collection("ContainerTests")]
public class DockerTests { }

public class QuickTests { }  // No collection
```

**Detection**: Collections found → Use collection-based splitting  
**Result**: 3 jobs (DatabaseTests, ContainerTests, Uncollected)

### Mode 3: Class-Based Splitting (Auto-Detected)

```xml
<PropertyGroup>
  <SplitTestsOnCI>true</SplitTestsOnCI>
  <TestClassNamesPrefix>Aspire.Templates.Tests</TestClassNamesPrefix>
</PropertyGroup>
```

```csharp
// No [Collection] attributes on any test class
public class Test1 { }
public class Test2 { }
public class Test3 { }
```

**Detection**: No collections found → Use class-based splitting  
**Result**: 3 jobs (Test1, Test2, Test3)

## Architecture

### Phase 1: Discovery (MSBuild)

```
ExtractTestClassNames Target
    ↓
Run: dotnet <assembly>.dll --list-tests
    ↓
Parse output with PowerShell helper
    ↓
Detect collections using regex
    ↓
    ├─ Collections found?
    │   └─ Write: collection:Name, uncollected:*
    │
    └─ No collections?
        └─ Write: class:FullClassName (one per class)
```

### Phase 2: Matrix Generation (PowerShell)

```
generate-test-matrix.ps1
    ↓
Read .tests.list file
    ↓
Parse entries
    ↓
    ├─ Type: collection
    │   └─ Generate: Collection jobs + Uncollected job
    │
    └─ Type: class
        └─ Generate: One job per class
```

## Implementation Components

### 1. PowerShell Discovery Helper

New script: `eng/scripts/extract-test-metadata.ps1`

Parses `--list-tests` output to detect collections.

### 2. Enhanced MSBuild Target

`ExtractTestClassNames` target calls PowerShell helper to detect mode.

### 3. Enhanced Matrix Generator

`generate-test-matrix.ps1` handles both collection and class entries.

## File Formats

### .tests.list Format (Auto-Generated)

**Collection-based mode** (collections detected):
```
collection:DatabaseTests
collection:ContainerTests
uncollected:*
```

**Class-based mode** (no collections):
```
class:Aspire.Templates.Tests.Test1
class:Aspire.Templates.Tests.Test2
class:Aspire.Templates.Tests.Test3
```

### Matrix Output

**Collection-based**:
```json
{
  "include": [
    {
      "type": "collection",
      "name": "DatabaseTests",
      "filterArg": "--filter-collection \"DatabaseTests\"",
      ...
    },
    {
      "type": "uncollected",
      "name": "UncollectedTests",
      "filterArg": "--filter-not-collection \"DatabaseTests\" ...",
      ...
    }
  ]
}
```

**Class-based**:
```json
{
  "include": [
    {
      "type": "class",
      "fullClassName": "Aspire.Templates.Tests.Test1",
      "filterArg": "--filter-class \"Aspire.Templates.Tests.Test1\"",
      ...
    },
    {
      "type": "class",
      "fullClassName": "Aspire.Templates.Tests.Test2",
      "filterArg": "--filter-class \"Aspire.Templates.Tests.Test2\"",
      ...
    }
  ]
}
```

## Benefits

1. **Zero Configuration**: Just set `SplitTestsOnCI=true` and it works
2. **Automatic Optimization**: Uses collections if present, falls back to classes
3. **Backward Compatible**: Existing projects work without changes
4. **Developer-Friendly**: Add `[Collection]` when needed, remove when not
5. **Flexible**: Can mix modes across different projects

## Configuration Properties

### Minimal Configuration

```xml
<PropertyGroup>
  <!-- Only these two are required! -->
  <SplitTestsOnCI>true</SplitTestsOnCI>
  <TestClassNamesPrefix>YourProject.Tests</TestClassNamesPrefix>
</PropertyGroup>
```

### Optional Overrides

```xml
<PropertyGroup>
  <!-- Override timeouts -->
  <SplitTestSessionTimeout>25m</SplitTestSessionTimeout>
  <SplitTestHangTimeout>12m</SplitTestHangTimeout>
  
  <!-- For collection mode only -->
  <UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
  <TestCollectionsToSkipSplitting>FastTests</TestCollectionsToSkipSplitting>
  
  <!-- Requirements -->
  <RequiresNugetsForSplitTests>false</RequiresNugetsForSplitTests>
  <RequiresTestSdkForSplitTests>false</RequiresTestSdkForSplitTests>
  <EnablePlaywrightInstallForSplitTests>false</EnablePlaywrightInstallForSplitTests>
</PropertyGroup>
```

## Implementation Steps

1. [Step 1: PowerShell Discovery Helper](./STEP_01_DISCOVERY_HELPER.md)
2. [Step 2: MSBuild Targets (v3)](./STEP_02_MSBUILD_TARGETS_V3.md)
3. [Step 3: Matrix Generator (v3)](./STEP_03_MATRIX_GENERATOR_V3.md)
4. [Step 4: GitHub Actions (No Changes)](./STEP_03_GITHUB_ACTIONS.md)
5. [Step 5: Project Configuration (v3)](./STEP_04_PROJECT_CONFIG_V3.md)
6. [Step 6: Testing & Migration](./STEP_05_TESTING_V3.md)

## Migration Examples

### Example 1: Aspire.Templates.Tests

**Current** (custom mechanism):
```xml
<ExtractTestClassNamesForHelix>true</ExtractTestClassNamesForHelix>
<ExtractTestClassNamesPrefix>Aspire.Templates.Tests</ExtractTestClassNamesPrefix>
```

**After v3** (unified, auto-detect):
```xml
<SplitTestsOnCI>true</SplitTestsOnCI>
<TestClassNamesPrefix>Aspire.Templates.Tests</TestClassNamesPrefix>
```

**Auto-detected mode**: Class-based (no collections in templates tests)  
**Result**: Same behavior as before (one job per test class)

### Example 2: Aspire.Hosting.Tests (NEW)

```xml
<SplitTestsOnCI>true</SplitTestsOnCI>
<TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
```

**Option A**: Leave tests as-is (no collections)
- **Auto-detected mode**: Class-based
- **Result**: One job per test class (~50 jobs)

**Option B**: Add collections to slow tests
```csharp
[Collection("DatabaseTests")]
public class PostgresTests { }

[Collection("DatabaseTests")]
public class MySqlTests { }

public class QuickTests { }  // No collection
```

- **Auto-detected mode**: Collection-based
- **Result**: 3 jobs (DatabaseTests, Uncollected with QuickTests, etc.)

## Decision Tree

```
Want to split tests?
│
├─ NO → Don't set SplitTestsOnCI
│        Result: 1 job (current behavior)
│
└─ YES → Set SplitTestsOnCI=true
         │
         Do you have logical test groups?
         │
         ├─ YES → Add [Collection] attributes
         │        Result: Auto-detected collection mode
         │        Jobs: N collections + 1 uncollected
         │
         └─ NO → Leave tests as-is
                 Result: Auto-detected class mode
                 Jobs: One per class
```

## Success Criteria

- ✅ Auto-detection works for both modes
- ✅ No breaking changes to existing projects
- ✅ Templates.Tests migrates cleanly
- ✅ Hosting.Tests can use either mode
- ✅ All 3 OSes work correctly
- ✅ Clear logging shows which mode was detected
- ✅ CI times reduced by 50%+ for long-running projects

## Next Steps

1. Review v3 plan
2. Implement discovery helper script
3. Update MSBuild targets with auto-detection
4. Update matrix generator to handle both modes
5. Test with both collection and class modes
6. Migrate Templates.Tests as proof-of-concept
7. Enable Hosting.Tests with collections
8. Document best practices

---

**Key Innovation**: v3 uses **automatic detection** to choose the optimal splitting strategy, eliminating configuration complexity while maintaining flexibility.