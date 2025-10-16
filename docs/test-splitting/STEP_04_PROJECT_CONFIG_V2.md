# Step 4: Project Configuration (v2 - Collection Support)

## Overview

Configure test projects to use collection-based splitting with examples showing how to optimize test execution.

## Configuration Properties

### Required Properties

```xml
<!-- Enable test splitting -->
<SplitTestsForCI>true</SplitTestsForCI>

<!-- Prefix for test discovery -->
<TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
```

### Optional Properties (v2 Enhancements)

```xml
<!-- Collection Management -->
<TestCollectionsToSkipSplitting>QuickTests;FastTests</TestCollectionsToSkipSplitting>

<!-- Timeouts for Collection Jobs -->
<SplitTestSessionTimeout>25m</SplitTestSessionTimeout>
<SplitTestHangTimeout>12m</SplitTestHangTimeout>

<!-- Timeouts for Uncollected Tests (usually shorter) -->
<UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
<UncollectedTestsHangTimeout>8m</UncollectedTestsHangTimeout>

<!-- Test Requirements -->
<RequiresNugetsForSplitTests>false</RequiresNugetsForSplitTests>
<RequiresTestSdkForSplitTests>false</RequiresTestSdkForSplitTests>
<EnablePlaywrightInstallForSplitTests>false</EnablePlaywrightInstallForSplitTests>
```

## Example 1: Aspire.Hosting.Tests (NEW - Collections)

### Project File Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- Enable collection-based splitting -->
    <SplitTestsForCI>true</SplitTestsForCI>
    <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
    
    <!-- Collections need more time (slow integration tests) -->
    <SplitTestSessionTimeout>30m</SplitTestSessionTimeout>
    <SplitTestHangTimeout>15m</SplitTestHangTimeout>
    
    <!-- Uncollected tests are faster (unit tests) -->
    <UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
    <UncollectedTestsHangTimeout>8m</UncollectedTestsHangTimeout>
    
    <!-- Standard integration test - no special requirements -->
    <RequiresNugetsForSplitTests>false</RequiresNugetsForSplitTests>
    <RequiresTestSdkForSplitTests>false</RequiresTestSdkForSplitTests>
  </PropertyGroup>

  <!-- Rest of project configuration... -->
</Project>
```

### Test Class Organization

```csharp
using Xunit;

namespace Aspire.Hosting.Tests;

// Slow database tests - group together
[Collection("DatabaseIntegration")]
public class PostgresLifecycleTests
{
    [Fact]
    public async Task CanStartPostgresContainer() 
    {
        // 2-3 minutes per test
    }
    
    [Fact]
    public async Task CanConnectToPostgres()
    {
        // 2-3 minutes per test
    }
}

[Collection("DatabaseIntegration")]
public class SqlServerLifecycleTests
{
    [Fact]
    public async Task CanStartSqlServerContainer()
    {
        // 2-3 minutes per test
    }
}

// Slow container tests - separate group
[Collection("ContainerLifecycle")]
public class DockerContainerTests
{
    [Fact]
    public async Task CanStartGenericContainer()
    {
        // 2-3 minutes per test
    }
    
    [Fact]
    public async Task CanStopContainer()
    {
        // 2 minutes per test
    }
}

[Collection("ContainerLifecycle")]
public class ContainerNetworkingTests
{
    [Fact]
    public async Task ContainersCanCommunicate()
    {
        // 3 minutes per test
    }
}

// Fast unit tests - NO collection attribute
public class ConfigurationTests
{
    [Fact]
    public void CanParseConfiguration()
    {
        // < 1 second
    }
    
    [Fact]
    public void CanValidateSettings()
    {
        // < 1 second
    }
}

public class UtilityTests
{
    [Fact]
    public void HelperMethodWorks()
    {
        // < 1 second
    }
}
```

### Expected CI Behavior

**Before** (1 job):
```
Aspire.Hosting.Tests: 55 minutes
```

**After** (3 jobs running in parallel):
```
Collection_DatabaseIntegration:  ~20 minutes (Postgres + SqlServer tests)
Collection_ContainerLifecycle:   ~15 minutes (Docker + Networking tests)
UncollectedTests:                ~5 minutes  (Config + Utility tests)
```

**Total CI Time**: ~20 minutes (60% reduction!)

## Example 2: Aspire.Templates.Tests (MIGRATED)

### Before (v1 - Class-based splitting)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- OLD: Required special properties -->
    <ExtractTestClassNamesForHelix>true</ExtractTestClassNamesForHelix>
    <ExtractTestClassNamesPrefix>Aspire.Templates.Tests</ExtractTestClassNamesPrefix>
    
    <TestUsingWorkloads>true</TestUsingWorkloads>
    <InstallWorkloadForTesting>true</InstallWorkloadForTesting>
  </PropertyGroup>
</Project>
```

### After (v2 - Collection-based splitting)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- NEW: Unified splitting mechanism -->
    <SplitTestsForCI>true</SplitTestsForCI>
    <TestClassNamesPrefix>Aspire.Templates.Tests</TestClassNamesPrefix>
    
    <!-- Template tests requirements -->
    <RequiresNugetsForSplitTests>true</RequiresNugetsForSplitTests>
    <RequiresTestSdkForSplitTests>true</RequiresTestSdkForSplitTests>
    <EnablePlaywrightInstallForSplitTests>true</EnablePlaywrightInstallForSplitTests>
    
    <!-- Collections: Playwright tests (slow) -->
    <SplitTestSessionTimeout>25m</SplitTestSessionTimeout>
    <SplitTestHangTimeout>15m</SplitTestHangTimeout>
    
    <!-- Uncollected: Build-only tests (faster) -->
    <UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
    <UncollectedTestsHangTimeout>10m</UncollectedTestsHangTimeout>
    
    <TestUsingWorkloads>true</TestUsingWorkloads>
    <InstallWorkloadForTesting>true</InstallWorkloadForTesting>
  </PropertyGroup>
</Project>
```

### Test Class Organization Strategy

```csharp
using Xunit;

namespace Aspire.Templates.Tests;

// Slow Playwright tests for starter template - group together
[Collection("StarterTemplateWithPlaywright")]
public class StarterTemplateProjectNamesTests
{
    // Each test: 3-5 minutes (Playwright browser automation)
}

[Collection("StarterTemplateWithPlaywright")]
public class StarterTemplateRunTests
{
    // Each test: 3-5 minutes
}

// Slow Playwright tests for basic template - separate group
[Collection("BasicTemplateWithPlaywright")]
public class BuildAndRunTemplateTests
{
    // Each test: 3-5 minutes
}

// Build-only tests (no Playwright) - NO collection
public class NewUpAndBuildStandaloneTemplateTests
{
    // Each test: 1-2 minutes (just dotnet build)
}

public class TemplateManifestTests
{
    // Each test: < 1 minute (metadata tests)
}
```

**Result**: 3 jobs
1. Collection_StarterTemplateWithPlaywright (~15 min)
2. Collection_BasicTemplateWithPlaywright (~12 min)
3. UncollectedTests (~5 min)

## Example 3: Simple Project (No Collections)

### When NOT to Use Collections

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- Enable splitting but no collections defined -->
    <SplitTestsForCI>true</SplitTestsForCI>
    <TestClassNamesPrefix>Aspire.MySqlConnector.Tests</TestClassNamesPrefix>
    
    <!-- All tests run together -->
    <UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
  </PropertyGroup>
</Project>
```

```csharp
// All test classes without [Collection] attribute
public class ConnectionTests { }
public class QueryTests { }
public class TransactionTests { }
```

**Result**: 1 job (UncollectedTests) running all tests

**When to use this**: 
- Project has < 15 minute total runtime
- All tests are similar speed
- No benefit from parallelization

## Example 4: Excluding Collections

### Scenario: Some Collections Shouldn't Split

```xml
<PropertyGroup>
  <SplitTestsForCI>true</SplitTestsForCI>
  <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
  
  <!-- These collections run in uncollected job -->
  <TestCollectionsToSkipSplitting>QuickIntegrationTests;FastSmokeTests</TestCollectionsToSkipSplitting>
</PropertyGroup>
```

```csharp
[Collection("SlowDatabaseTests")]
public class SlowTests { }  // Gets own job

[Collection("QuickIntegrationTests")]  // Excluded from splitting
public class QuickTests { }  // Runs in UncollectedTests

public class OtherTests { }  // Runs in UncollectedTests
```

**Result**: 2 jobs
1. Collection_SlowDatabaseTests
2. UncollectedTests (includes QuickIntegrationTests + OtherTests)

## Decision Matrix: Should You Use Collections?

### ✅ Use Collections When:

| Scenario | Example |
|----------|---------|
| **Shared expensive setup** | Database containers that multiple test classes use |
| **Long-running integration tests** | Tests that take 2+ minutes each |
| **Logical test grouping** | All Azure tests, all Docker tests, etc. |
| **Similar resource needs** | Tests that all need Playwright, or all need databases |

### ❌ Don't Use Collections When:

| Scenario | Reason |
|----------|--------|
| **Fast unit tests** | Overhead isn't worth it; let them run together |
| **< 5 total test classes** | Not enough parallelization benefit |
| **Tests need isolation** | Collections share fixtures which may cause conflicts |
| **Total runtime < 15 min** | Single job is fast enough |

## Migration Checklist

### For Each Long-Running Project:

- [ ] Analyze test suite duration
- [ ] Identify slow test groups (> 10 min combined)
- [ ] Add `[Collection("GroupName")]` to slow test classes
- [ ] Keep fast tests without collection attribute
- [ ] Update .csproj with split configuration
- [ ] Set appropriate timeouts
- [ ] Test locally first
- [ ] Monitor CI times after merge

## Best Practices

### 1. Collection Naming

```csharp
// ✅ Good: Descriptive, indicates purpose
[Collection("DatabaseIntegrationTests")]
[Collection("ContainerLifecycleTests")]
[Collection("PlaywrightAutomationTests")]

// ❌ Bad: Too vague or too specific
[Collection("Tests")]  // Too vague
[Collection("PostgresTest1")]  // Too specific
```

### 2. Collection Size

```csharp
// ✅ Good: Multiple related test classes in one collection
[Collection("DatabaseTests")]
public class PostgresTests { /* 10 tests */ }

[Collection("DatabaseTests")]
public class MySqlTests { /* 8 tests */ }

[Collection("DatabaseTests")]
public class SqlServerTests { /* 12 tests */ }
// Total: 30 tests, ~20 minutes - good parallelization unit

// ❌ Bad: One test class per collection
[Collection("PostgresTests")]
public class PostgresTests { /* 10 tests */ }

[Collection("MySqlTests")]
public class MySqlTests { /* 8 tests */ }
// Too granular, overhead not worth it
```

### 3. Timeout Configuration

```xml
<!-- Collections: Slow integration tests -->
<SplitTestSessionTimeout>30m</SplitTestSessionTimeout>

<!-- Uncollected: Fast unit tests -->
<UncollectedTestsSessionTimeout>10m</UncollectedTestsSessionTimeout>
```

### 4. Test Isolation

```csharp
// ✅ Good: Tests in same collection can share fixtures
[Collection("DatabaseTests")]
public class PostgresTests : IClassFixture<PostgresContainerFixture>
{
    // Fixture is shared across collection
}

[Collection("DatabaseTests")]
public class MySqlTests : IClassFixture<PostgresContainerFixture>
{
    // Same fixture instance - efficient!
}

// ❌ Bad: Tests that MUST be isolated shouldn't share collection
[Collection("IsolatedTests")]  // Don't do this
public class Test1 { /* Modifies global state */ }

[Collection("IsolatedTests")]  // Will conflict with Test1
public class Test2 { /* Also modifies global state */ }
```

## Validation After Configuration

### 1. Build Locally

```bash
dotnet build tests/YourProject.Tests/YourProject.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsForCI=true \
  -p:TestClassNamesPrefix=YourProject.Tests
```

### 2. Check Generated Files

```bash
# Should see:
ls artifacts/helix/YourProject.Tests.tests.list
ls artifacts/helix/YourProject.Tests.tests.metadata.json

# Content should be:
cat artifacts/helix/YourProject.Tests.tests.list
# collection:YourCollection1
# collection:YourCollection2
# uncollected:*
```

### 3. Generate Matrix

```bash
pwsh eng/scripts/generate-test-matrix.ps1 \
  -TestListsDirectory ./artifacts/helix \
  -OutputDirectory ./artifacts/test-matrices \
  -BuildOs linux
```

### 4. Verify Matrix

```bash
cat artifacts/test-matrices/split-tests-matrix.json | jq '.include[] | {name, filterArg}'
```

**Expected output**:
```json
{
  "name": "YourCollection1",
  "filterArg": "--filter-collection \"YourCollection1\""
}
{
  "name": "YourCollection2",
  "filterArg": "--filter-collection \"YourCollection2\""
}
{
  "name": "UncollectedTests",
  "filterArg": "--filter-not-collection \"YourCollection1\" --filter-not-collection \"YourCollection2\""
}
```

## Next Steps

Proceed to [Step 5: Testing & Validation (v2)](./STEP_05_TESTING_V2.md)