# Step 4: Project Configuration (v3 - Simplified)

## Overview

With v3's auto-detection, project configuration is minimal. Just set two properties and the system automatically detects whether to use collection or class-based splitting.

## Minimal Configuration

### Required Properties (Only 2!)

```xml
<PropertyGroup>
  <!-- Enable splitting -->
  <SplitTestsOnCI>true</SplitTestsOnCI>
  
  <!-- Set prefix for test discovery -->
  <TestClassNamesPrefix>YourProject.Tests</TestClassNamesPrefix>
</PropertyGroup>
```

That's it! The system auto-detects collections and chooses the optimal strategy.

## Configuration Examples

### Example 1: Aspire.Hosting.Tests (NEW - With Collections)

#### Step 1: Configure Project

```xml name=tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- Enable auto-detection splitting -->
    <SplitTestsOnCI>true</SplitTestsOnCI>
    <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
    
    <!-- Optional: Custom timeouts -->
    <SplitTestSessionTimeout>30m</SplitTestSessionTimeout>
    <SplitTestHangTimeout>15m</SplitTestHangTimeout>
    <UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
    <UncollectedTestsHangTimeout>8m</UncollectedTestsHangTimeout>
  </PropertyGroup>

  <!-- Rest of project... -->
</Project>
```

#### Step 2: Add Collections to Test Classes

```csharp
using Xunit;

namespace Aspire.Hosting.Tests;

// Group slow database tests together
[Collection("DatabaseIntegration")]
public class PostgresLifecycleTests
{
    [Fact]
    public async Task CanStartPostgresContainer() 
    {
        // Test implementation
    }
}

[Collection("DatabaseIntegration")]
public class MySqlLifecycleTests
{
    [Fact]
    public async Task CanStartMySqlContainer()
    {
        // Test implementation
    }
}

// Group container tests together
[Collection("ContainerLifecycle")]
public class DockerContainerTests
{
    [Fact]
    public async Task CanStartGenericContainer()
    {
        // Test implementation
    }
}

// Fast tests - NO collection attribute
public class ConfigurationTests
{
    [Fact]
    public void CanParseConfiguration()
    {
        // Fast unit test
    }
}

public class UtilityTests
{
    [Fact]
    public void HelperMethodWorks()
    {
        // Fast unit test
    }
}
```

#### Result

**Auto-detected mode**: Collection (2 collections found)  
**CI Jobs**: 3
- `Collection_DatabaseIntegration` (Postgres + MySQL tests)
- `Collection_ContainerLifecycle` (Docker tests)
- `Uncollected` (Configuration + Utility tests)

**Before**: 1 job, 60 minutes  
**After**: 3 parallel jobs, ~25 minutes (58% reduction)

### Example 2: Aspire.Templates.Tests (MIGRATE from Old System)

#### Before (Custom Mechanism)

```xml
<PropertyGroup>
  <!-- OLD: Custom properties -->
  <ExtractTestClassNamesForHelix>true</ExtractTestClassNamesForHelix>
  <ExtractTestClassNamesPrefix>Aspire.Templates.Tests</ExtractTestClassNamesPrefix>
  <TestArchiveTestsDir>$(TestArchiveTestsDirForTemplateTests)</TestArchiveTestsDir>
</PropertyGroup>
```

#### After (Unified v3 Mechanism)

```xml name=tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- NEW: Unified mechanism with auto-detection -->
    <SplitTestsOnCI>true</SplitTestsOnCI>
    <TestClassNamesPrefix>Aspire.Templates.Tests</TestClassNamesPrefix>
    
    <!-- Templates need packages and SDK -->
    <RequiresNugetsForSplitTests>true</RequiresNugetsForSplitTests>
    <RequiresTestSdkForSplitTests>true</RequiresTestSdkForSplitTests>
    <EnablePlaywrightInstallForSplitTests>true</EnablePlaywrightInstallForSplitTests>
    
    <!-- Timeouts -->
    <SplitTestSessionTimeout>20m</SplitTestSessionTimeout>
    <SplitTestHangTimeout>12m</SplitTestHangTimeout>

    <!-- Keep existing properties -->
    <TestUsingWorkloads>true</TestUsingWorkloads>
    <InstallWorkloadForTesting>true</InstallWorkloadForTesting>
    <XunitRunnerJson>xunit.runner.json</XunitRunnerJson>
    <NoWarn>$(NoWarn);xUnit1051</NoWarn>
  </PropertyGroup>

  <Import Project="..\Shared\TemplatesTesting\Aspire.Shared.TemplatesTesting.targets" />

  <ItemGroup>
    <Compile Include="$(RepoRoot)src\Aspire.Hosting.Redis\RedisContainerImageTags.cs" />
    <Compile Include="$(RepoRoot)src\Shared\KnownConfigNames.cs" Link="KnownConfigNames.cs" />
    <PackageReference Include="Microsoft.DotNet.XUnitV3Extensions" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
  </ItemGroup>
</Project>
```

#### Test Classes (No Changes Needed)

```csharp
// Existing test classes without [Collection] attributes
public class BuildAndRunTemplateTests { }
public class EmptyTemplateRunTests { }
public class StarterTemplateRunTests { }
// ... etc
```

#### Result

**Auto-detected mode**: Class (no collections found)  
**CI Jobs**: 12 (one per test class)  
**Behavior**: Identical to old system, but using unified infrastructure

### Example 3: Simple Project (No Splitting Needed)

```xml name=tests/Aspire.MySqlConnector.Tests/Aspire.MySqlConnector.Tests.csproj
<PropertyGroup>
  <!-- Don't set SplitTestsOnCI - runs as single job -->
  <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
</PropertyGroup>
```

**Result**: 1 job (existing behavior, no splitting)

## Optional Configuration Properties

### Timeouts

```xml
<!-- Default timeout for collection/class jobs -->
<SplitTestSessionTimeout>20m</SplitTestSessionTimeout>
<SplitTestHangTimeout>10m</SplitTestHangTimeout>

<!-- Shorter timeout for uncollected tests (usually faster) -->
<UncollectedTestsSessionTimeout>15m</UncollectedTestsSessionTimeout>
<UncollectedTestsHangTimeout>8m</UncollectedTestsHangTimeout>
```

### Test Requirements

```xml
<!-- Does this test need built NuGet packages? -->
<RequiresNugetsForSplitTests>true</RequiresNugetsForSplitTests>

<!-- Does this test need test SDK installed? -->
<RequiresTestSdkForSplitTests>true</RequiresTestSdkForSplitTests>

<!-- Does this test need Playwright browsers? -->
<EnablePlaywrightInstallForSplitTests>true</EnablePlaywrightInstallForSplitTests>
```

### Collection Management

```xml
<!-- Exclude certain collections from getting their own jobs -->
<TestCollectionsToSkipSplitting>FastTests;QuickTests</TestCollectionsToSkipSplitting>
```

These collections will run in the `Uncollected` job instead.

## Decision Guide

### Should I Enable Splitting?

```
Is total test time > 15 minutes?
│
├─ NO → Don't enable SplitTestsOnCI
│        Overhead not worth it
│
└─ YES → Enable SplitTestsOnCI=true
         │
         Do you have logical test groups?
         │
         ├─ YES → Add [Collection] attributes
         │        System auto-detects: Collection mode
         │        Result: Fewer jobs, better parallelization
         │
         └─ NO → Leave tests as-is
                 System auto-detects: Class mode
                 Result: One job per class
```

### Collection Size Guidelines

**Good Collection** (15-30 minutes):
```csharp
[Collection("DatabaseTests")]
public class PostgresTests { /* 20 tests, 8 min */ }

[Collection("DatabaseTests")]
public class MySqlTests { /* 15 tests, 7 min */ }

[Collection("DatabaseTests")]
public class SqlServerTests { /* 25 tests, 10 min */ }

// Total: ~25 minutes - ideal for one job
```

**Too Small** (< 5 minutes):
```csharp
[Collection("QuickTest")]
public class OneTest { /* 2 tests, 1 min */ }

// Don't create collections for fast tests
// Let them run in the uncollected job
```

**Too Large** (> 45 minutes):
```csharp
[Collection("AllDatabaseTests")]
public class Test1 { /* 100 tests */ }
public class Test2 { /* 100 tests */ }
// ...

// Split into multiple smaller collections instead
```

## Migration Checklist

### For Each Long-Running Project:

- [ ] Measure current test duration
- [ ] If > 15 min, enable `SplitTestsOnCI=true`
- [ ] Set `TestClassNamesPrefix`
- [ ] (Optional) Add `[Collection]` to slow test groups
- [ ] Test locally (see Step 5)
- [ ] Create PR
- [ ] Monitor CI times after merge

### Specific Migration: Aspire.Templates.Tests

- [ ] Replace `ExtractTestClassNamesForHelix` with `SplitTestsOnCI`
- [ ] Keep `TestClassNamesPrefix` (same name)
- [ ] Add `RequiresNugetsForSplitTests=true`
- [ ] Add `RequiresTestSdkForSplitTests=true`
- [ ] Add `EnablePlaywrightInstallForSplitTests=true`
- [ ] Remove `TestArchiveTestsDir` override
- [ ] Test locally
- [ ] Verify same number of jobs in CI

## Next Steps

Proceed to [Step 5: Testing & Validation](./STEP_05_TESTING_V3.md)