# Step 4: Project Configuration

## Overview

Configure test projects to use the new unified splitting mechanism. This step shows how to migrate existing projects and enable new ones.

## Configuration Properties

### Required Properties (for splitting)

```xml
<!-- Enable test splitting -->
<SplitTestsForCI>true</SplitTestsForCI>

<!-- Prefix for test class extraction (must match namespace) -->
<TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
```

### Optional Properties

```xml
<!-- Exclude specific test classes from splitting -->
<TestClassNamesToSkipTests>QuickTest1;QuickTest2</TestClassNamesToSkipTests>

<!-- Custom timeouts for split test jobs -->
<SplitTestSessionTimeout>25m</SplitTestSessionTimeout>
<SplitTestHangTimeout>12m</SplitTestHangTimeout>

<!-- Indicate split tests need packages -->
<RequiresNugetsForSplitTests>true</RequiresNugetsForSplitTests>

<!-- Indicate split tests need test SDK -->
<RequiresTestSdkForSplitTests>true</RequiresTestSdkForSplitTests>

<!-- Enable Playwright browser installation -->
<EnablePlaywrightInstallForSplitTests>true</EnablePlaywrightInstallForSplitTests>
```

## Migration: Aspire.Templates.Tests

### Before (Custom Implementation)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <TestUsingWorkloads>true</TestUsingWorkloads>
    <InstallWorkloadForTesting>true</InstallWorkloadForTesting>

    <XunitRunnerJson>xunit.runner.json</XunitRunnerJson>
    <TestArchiveTestsDir>$(TestArchiveTestsDirForTemplateTests)</TestArchiveTestsDir>

    <!-- OLD: Template-specific properties -->
    <ExtractTestClassNamesForHelix Condition="'$(ContinuousIntegrationBuild)' == 'true' or '$(PrepareForHelix)' == 'true'">true</ExtractTestClassNamesForHelix>
    <ExtractTestClassNamesPrefix>Aspire.Templates.Tests</ExtractTestClassNamesPrefix>

    <NoWarn>$(NoWarn);xUnit1051</NoWarn>
    <SkipTests Condition=" '$(RunQuarantinedTests)' == 'true' ">true</SkipTests>
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

### After (Unified Mechanism)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <TestUsingWorkloads>true</TestUsingWorkloads>
    <InstallWorkloadForTesting>true</InstallWorkloadForTesting>

    <XunitRunnerJson>xunit.runner.json</XunitRunnerJson>

    <!-- NEW: Use unified splitting mechanism -->
    <SplitTestsForCI>true</SplitTestsForCI>
    <TestClassNamesPrefix>Aspire.Templates.Tests</TestClassNamesPrefix>
    
    <!-- Configure split test requirements -->
    <RequiresNugetsForSplitTests>true</RequiresNugetsForSplitTests>
    <RequiresTestSdkForSplitTests>true</RequiresTestSdkForSplitTests>
    <EnablePlaywrightInstallForSplitTests>true</EnablePlaywrightInstallForSplitTests>
    
    <!-- Custom timeouts for template tests -->
    <SplitTestSessionTimeout>20m</SplitTestSessionTimeout>
    <SplitTestHangTimeout>12m</SplitTestHangTimeout>

    <NoWarn>$(NoWarn);xUnit1051</NoWarn>
    <SkipTests Condition=" '$(RunQuarantinedTests)' == 'true' ">true</SkipTests>
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

### Changes Summary

- ✅ Replace `ExtractTestClassNamesForHelix` with `SplitTestsForCI`
- ✅ Keep `TestClassNamesPrefix` (same property name)
- ✅ Add `RequiresNugetsForSplitTests=true`
- ✅ Add `RequiresTestSdkForSplitTests=true`
- ✅ Add `EnablePlaywrightInstallForSplitTests=true`
- ✅ Add timeout configurations
- ✅ Remove `TestArchiveTestsDir` override (use default)

## New Project: Aspire.Hosting.Tests

### Complete Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(DefaultTargetFramework)</TargetFramework>

    <!-- Enable test splitting -->
    <SplitTestsForCI>true</SplitTestsForCI>
    <TestClassNamesPrefix>Aspire.Hosting.Tests</TestClassNamesPrefix>
    
    <!-- Hosting tests are long-running -->
    <SplitTestSessionTimeout>25m</SplitTestSessionTimeout>
    <SplitTestHangTimeout>15m</SplitTestHangTimeout>
    
    <!-- Hosting tests don't need special requirements -->
    <RequiresNugetsForSplitTests>false</RequiresNugetsForSplitTests>
    <RequiresTestSdkForSplitTests>false</RequiresTestSdkForSplitTests>
    <EnablePlaywrightInstallForSplitTests>false</EnablePlaywrightInstallForSplitTests>
    
    <!-- Optional: Exclude very quick tests from splitting -->
    <!-- <TestClassNamesToSkipTests>QuickTest1;QuickTest2</TestClassNamesToSkipTests> -->
  </PropertyGroup>

  <!-- Rest of project configuration... -->
</Project>
```

## OS-Specific Opt-In/Out

### Example: Linux-Only Splitting

Some projects may only need splitting on Linux (e.g., Docker tests):

```xml
<PropertyGroup>
  <!-- Enable splitting only on Linux -->
  <SplitTestsForCI Condition="'$(BuildOs)' == 'linux'">true</SplitTestsForCI>
  <TestClassNamesPrefix>Aspire.Docker.Tests</TestClassNamesPrefix>
  
  <!-- On Windows/macOS, run normally (no splitting) -->
  <RunOnGithubActionsWindows>true</RunOnGithubActionsWindows>
  <RunOnGithubActionsMacOS>false</RunOnGithubActionsMacOS>
  <RunOnGithubActionsLinux>true</RunOnGithubActionsLinux>
</PropertyGroup>
```

This creates:
- **Linux**: Split into multiple jobs (one per class)
- **Windows**: Single job (no splitting)
- **macOS**: Doesn't run at all

## Projects to Enable Splitting

### High Priority (Long-Running)

1. **Aspire.Templates.Tests** ✅ (Already has splitting, migrate to new mechanism)
   - Currently: ~15 test classes
   - Timeout: 20m
   - Needs: Packages, SDK, Playwright

2. **Aspire.Hosting.Tests** 🎯 (Primary target)
   - Estimated: 50+ test classes
   - Timeout: 25m  
   - Needs: None (regular integration test)

3. **Aspire.Hosting.*.Tests** (if long-running)
   - Aspire.Hosting.Azure.Tests
   - Aspire.Hosting.Postgres.Tests
   - etc.

### Medium Priority

4. Other integration tests if they exceed 15 minutes

### Low Priority

- Unit tests (usually fast enough)
- Tests with < 5 test classes (overhead not worth it)

## Configuration Decision Tree

```
Is the test project slow (>15 minutes)?
│
├─ NO → Don't enable splitting
│        (Keep as regular test)
│
└─ YES → Does it have >5 test classes?
         │
         ├─ NO → Don't enable splitting
         │        (Won't benefit from parallelization)
         │
         └─ YES → Enable splitting!
                  │
                  ├─ Set SplitTestsForCI=true
                  ├─ Set TestClassNamesPrefix
                  ├─ Set custom timeouts if needed
                  └─ Set requirements (packages/SDK/etc.)
```

## Validation Checklist

Before