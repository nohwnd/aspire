# Test Splitting Implementation Plan v2 - Hybrid Collection + Class Splitting

**Date**: 2025-10-16  
**Author**: @radical  
**Objective**: Implement a flexible test splitting mechanism that supports:
- ✅ Individual jobs per xUnit Collection (for grouped tests)
- ✅ ONE job for all uncollected tests (catch-all)
- ✅ Works across all 3 OSes (Linux, macOS, Windows)

## Overview

This v2 plan enhances the original with **hybrid collection-based splitting**:

### Splitting Strategies

```
Test Project
    ├─ Tests with [Collection("Group1")] → 1 job (all Group1 tests)
    ├─ Tests with [Collection("Group2")] → 1 job (all Group2 tests)
    └─ All other tests (no collection)   → 1 job (ClassA + ClassB + ClassC + ...)
```

### Example with 3 Jobs

```
Aspire.Hosting.Tests
    ├─ [Collection("SlowDatabaseTests")]     → Job 1: Collection_SlowDatabaseTests
    ├─ [Collection("IntegrationTests")]      → Job 2: Collection_IntegrationTests  
    └─ QuickTests, FastTests, UnitTests...   → Job 3: UncollectedTests
       (no collection attribute)
```

**Total**: 3 parallel jobs instead of 1 monolithic job

### xUnit Collection Features Used

- `[Collection("name")]` attribute to group test classes
- `--filter-collection <name>` to run specific collection
- `--filter-not-collection <name1> --filter-not-collection <name2>` to run everything NOT in collections

## Architecture Changes

### Test Discovery Output Format

The `.tests.list` file now includes collections discovered:

```
# Format: <Type>:<Name>
collection:SlowDatabaseTests
collection:IntegrationTests
uncollected:*
```

Note: We don't list individual classes anymore - just collections + one uncollected entry.

### Matrix Entry Structure

```json
{
  "include": [
    {
      "type": "collection",
      "name": "SlowDatabaseTests",
      "filterArg": "--filter-collection SlowDatabaseTests",
      "shortname": "Collection_SlowDatabaseTests",
      "testSessionTimeout": "30m",
      "testHangTimeout": "15m"
    },
    {
      "type": "collection",
      "name": "IntegrationTests",
      "filterArg": "--filter-collection IntegrationTests",
      "shortname": "Collection_IntegrationTests",
      "testSessionTimeout": "25m",
      "testHangTimeout": "12m"
    },
    {
      "type": "uncollected",
      "name": "UncollectedTests",
      "filterArg": "--filter-not-collection SlowDatabaseTests --filter-not-collection IntegrationTests",
      "shortname": "Uncollected",
      "testSessionTimeout": "20m",
      "testHangTimeout": "10m"
    }
  ]
}
```

## Key Benefits

### Efficiency
- **Fewer jobs**: Only create jobs for collections + 1 catch-all
- **Less overhead**: No job-per-class overhead for fast tests
- **Better resource usage**: Group related tests with shared fixtures

### Flexibility
- **Opt-in granularity**: Only split out slow/problematic test groups
- **Simple default**: Tests without collections run normally together
- **Developer control**: Use `[Collection]` to optimize as needed

### Backward Compatible
- **No collections?** → 1 job (current behavior)
- **All collections?** → N jobs (one per collection)
- **Mixed?** → N+1 jobs (collections + uncollected)

## Implementation Steps

See updated files:
1. [Step 1: MSBuild Targets (v2)](./STEP_01_MSBUILD_TARGETS_V2.md)
2. [Step 2: PowerShell Script (v2)](./STEP_02_POWERSHELL_SCRIPT_V2.md)
3. [Step 3: GitHub Actions (No Changes)](./STEP_03_GITHUB_ACTIONS.md)
4. [Step 4: Project Configuration (v2)](./STEP_04_PROJECT_CONFIG_V2.md)
5. [Step 5: Testing & Validation (v2)](./STEP_05_TESTING_V2.md)

## Usage Examples

### Example 1: No Collections (Simple Case)

```xml
<PropertyGroup>
  <SplitTestsForCI>true</SplitTestsForCI>
  <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
</PropertyGroup>
```

```csharp
// No collection attributes
public class QuickTests { }
public class FastTests { }
public class UnitTests { }
```

**Result**: 1 job running all tests (equivalent to not splitting)

### Example 2: Hybrid Splitting (Recommended)

```xml
<PropertyGroup>
  <SplitTestsForCI>true</SplitTestsForCI>
  <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
</PropertyGroup>
```

```csharp
// Slow database tests - group together
[Collection("DatabaseTests")]
public class PostgresTests 
{
    // 50 tests, 15 minutes
}

[Collection("DatabaseTests")]
public class MySqlTests 
{
    // 30 tests, 10 minutes
}

// Slow container tests - separate group
[Collection("ContainerTests")]
public class DockerTests 
{
    // 40 tests, 12 minutes
}

// Fast tests - no collection (run together)
public class QuickTests 
{
    // 100 tests, 2 minutes
}

public class UnitTests 
{
    // 200 tests, 3 minutes
}
```

**Result**: 3 parallel jobs
1. **Collection_DatabaseTests**: PostgresTests + MySqlTests (~25 min)
2. **Collection_ContainerTests**: DockerTests (~12 min)
3. **UncollectedTests**: QuickTests + UnitTests (~5 min)

**Total CI time**: ~25 min (previously 55+ min)

### Example 3: All Collections (Maximum Splitting)

```csharp
[Collection("PostgresTests")]
public class PostgresTests { }

[Collection("MySqlTests")]
public class MySqlTests { }

[Collection("DockerTests")]
public class DockerTests { }
```

**Result**: 3 jobs (one per collection), no uncollected job

### Example 4: Exclude Certain Collections

```xml
<PropertyGroup>
  <SplitTestsForCI>true</SplitTestsForCI>
  <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
  
  <!-- These collections run in the uncollected catch-all -->
  <TestCollectionsToSkipSplitting>QuickTests;FastTests</TestCollectionsToSkipSplitting>
</PropertyGroup>
```

```csharp
[Collection("SlowTests")]
public class SlowTests { }  // Gets own job

[Collection("QuickTests")]
public class QuickTests { }  // Runs in UncollectedTests job

public class OtherTests { }  // Runs in UncollectedTests job
```

**Result**: 2 jobs
1. **Collection_SlowTests**
2. **UncollectedTests** (QuickTests + OtherTests)

## Configuration Properties

### New in v2

```xml
<!-- Optional: Disable collection detection (use v1 class-based splitting) -->
<DisableCollectionBasedSplitting>false</DisableCollectionBasedSplitting>

<!-- Optional: Exclude certain collections from getting their own jobs -->
<TestCollectionsToSkipSplitting>Collection1;Collection2</TestCollectionsToSkipSplitting>

<!-- Optional: Custom timeout for uncollected tests job -->
<UncollectedTestsSessionTimeout>20m</UncollectedTestsSessionTimeout>
<UncollectedTestsHangTimeout>10m</UncollectedTestsHangTimeout>
```

### Per-Collection Timeouts (Advanced)

```xml
<!-- Override timeout for specific collections -->
<PropertyGroup>
  <TestCollection_DatabaseTests_SessionTimeout>30m</TestCollection_DatabaseTests_SessionTimeout>
  <TestCollection_IntegrationTests_SessionTimeout>25m</TestCollection_IntegrationTests_SessionTimeout>
</PropertyGroup>
```

## Decision Tree

```
Is the test project slow (>15 minutes)?
│
├─ NO → Don't enable splitting
│        (Keep as regular test)
│
└─ YES → Do you have groups of slow tests?
         │
         ├─ NO → Don't enable splitting OR use simple splitting
         │        (All tests in one job is fine)
         │
         └─ YES → Use collection-based splitting!
                  │
                  Step 1: Add [Collection("GroupName")] to slow test groups
                  Step 2: Set SplitTestsForCI=true
                  Step 3: Set TestClassNamesPrefix
                  Step 4: Leave fast tests without collection attribute
                  │
                  Result: N+1 jobs (N collections + 1 uncollected)
```

## Migration Strategy

### Phase 1: Infrastructure (Week 1)
- Implement v2 MSBuild targets with collection discovery
- Update PowerShell script to generate collection-based matrices
- Test with example project (no actual collections yet)

### Phase 2: Migrate Templates.Tests (Week 2)
- Keep NO collections initially (verify 1 job = current behavior)
- Optionally add collections if beneficial
- Validate backward compatibility

### Phase 3: Enable Hosting.Tests (Week 3)
- Analyze test suite to identify slow groups
- Add `[Collection]` attributes to slow test groups
- Enable `SplitTestsForCI=true`
- Compare CI times before/after

### Phase 4: Rollout & Optimize (Week 4)
- Apply to other long-running projects
- Fine-tune collection groupings based on actual times
- Document best practices

## Best Practices

### When to Use Collections

✅ **DO** use collections for:
- Tests that share expensive setup/teardown
- Tests that use the same test fixtures
- Long-running integration tests that can be grouped logically
- Tests that have similar resource requirements

❌ **DON'T** use collections for:
- Fast unit tests (let them run together in uncollected job)
- Tests that should be isolated
- Creating too many tiny collections (overhead not worth it)

### Recommended Groupings

```csharp
// Good: Logical grouping of slow tests
[Collection("DatabaseIntegrationTests")]
public class PostgresIntegrationTests { }

[Collection("DatabaseIntegrationTests")]
public class SqlServerIntegrationTests { }

// Good: Resource-specific grouping
[Collection("DockerContainerTests")]
public class ContainerLifecycleTests { }

[Collection("DockerContainerTests")]
public class ContainerNetworkingTests { }

// Bad: Too granular (defeats the purpose)
[Collection("PostgresTest1")]
public class PostgresTest1 { }

[Collection("PostgresTest2")]
public class PostgresTest2 { }
```

## Expected Outcomes

### Before (Monolithic)
```
Aspire.Hosting.Tests: 1 job, 60 minutes
```

### After (Collection-Based Splitting)
```
Collection_DatabaseTests:   1 job, 25 minutes
Collection_ContainerTests:  1 job, 20 minutes
Collection_AzureTests:      1 job, 15 minutes
UncollectedTests:           1 job, 10 minutes
```

**Total CI time**: ~25 minutes (jobs run in parallel)  
**Job count**: 4 jobs (manageable)  
**Time saved**: 35 minutes (58% reduction)

## Success Criteria

- ✅ All OSes generate correct collection-based matrices
- ✅ Collection tests run together in single jobs
- ✅ Uncollected tests run together in one job
- ✅ No tests are accidentally skipped
- ✅ CI time for long-running projects reduced by 50%+
- ✅ Number of jobs remains manageable (<10 per project per OS)
- ✅ Works on both GitHub Actions and Azure DevOps

## Rollback Plan

If issues arise:
1. Set `DisableCollectionBasedSplitting=true` to use v1 class-based splitting
2. Or set `SplitTestsForCI=false` to disable all splitting
3. No YAML changes needed (matrix adapts automatically)

## Next Steps

1. Review this updated v2 plan
2. Implement Step 1 (MSBuild targets with collection discovery)
3. Implement Step 2 (PowerShell script with collection matrix generation)
4. Test with sample collections
5. Roll out to Hosting.Tests
6. Monitor and optimize

---

**Key Innovation**: v2 uses xUnit collections to create **logical test groups** while keeping fast tests together, resulting in optimal parallelization with minimal job overhead.