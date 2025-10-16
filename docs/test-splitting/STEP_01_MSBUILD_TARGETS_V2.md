# Step 1: MSBuild Targets Implementation (v2 - Collection Support)

## Overview

Enhanced MSBuild targets that discover xUnit collections and generate a hybrid matrix with:
- One job per collection
- One job for all uncollected tests

## File: `tests/Directory.Build.targets`

### Complete Enhanced Target

```xml
<!-- Location: tests/Directory.Build.targets -->

<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" />
  <Import Project="$(TestsSharedRepoTestingDir)Aspire.RepoTesting.targets" />

  <!-- EXISTING PropertyGroups for XunitRunnerJson, TestingPlatformCommandLineArguments, etc. -->
  <!-- KEEP AS IS -->

  <PropertyGroup>
    <!-- Use a separate xunit.runner.json for helix that disables parallel test runs -->
    <XunitRunnerJson Condition="'$(XunitRunnerJson)' == '' and '$(PrepareForHelix)' == 'true'">$(RepoRoot)tests\helix\xunit.runner.json</XunitRunnerJson>
    <XunitRunnerJson Condition="'$(XunitRunnerJson)' == ''">$(RepositoryEngineeringDir)testing\xunit.runner.json</XunitRunnerJson>

    <!-- Properties to allow control tests to run, useful for local command line runs -->
    <TestingPlatformCommandLineArguments Condition="'$(TestMethod)' != ''">$(TestingPlatformCommandLineArguments) --filter-method $(TestMethod)</TestingPlatformCommandLineArguments>
    <TestingPlatformCommandLineArguments Condition="'$(TestClass)' != ''">$(TestingPlatformCommandLineArguments) --filter-class $(TestClass)</TestingPlatformCommandLineArguments>
    <TestingPlatformCommandLineArguments Condition="'$(TestNamespace)' != ''">$(TestingPlatformCommandLineArguments) --filter-namespace $(TestNamespace)</TestingPlatformCommandLineArguments>

    <TestCaptureOutput Condition="'$(TestCaptureOutput)' == '' and '$(ContinuousIntegrationBuild)' == 'true'">true</TestCaptureOutput>
    <TestCaptureOutput Condition="'$(TestCaptureOutput)' == ''">false</TestCaptureOutput>
    
    <!-- NEW: Default to collection-based splitting (can be disabled) -->
    <DisableCollectionBasedSplitting Condition="'$(DisableCollectionBasedSplitting)' == ''">false</DisableCollectionBasedSplitting>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(XunitRunnerJson)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <!-- KEEP EXISTING ZipTestArchive target as-is -->
  <Target Name="ZipTestArchive" AfterTargets="Build"
          Condition=" '$(IsTestProject)' == 'true' and '$(PrepareForHelix)' == 'true' and '$(RunOnAzdoHelix)' == 'true' and '$(IsTestUtilityProject)' != 'true' and '$(IsCrossTargetingBuild)' != 'true' ">
    <Error Condition="'$(TestArchiveTestsDir)' == ''" Text="TestArchiveTestsDir property to archive the test folder must be set." />
    <PropertyGroup>
      <TestsArchiveSourceDir Condition="'$(TestsArchiveSourceDir)' == ''">$(OutDir)</TestsArchiveSourceDir>
      <ZipTestArchiveTfm></ZipTestArchiveTfm>
      <ZipTestArchiveTfm Condition="'$(TargetFramework)' != '$(DefaultTargetFramework)'">-$(TargetFramework)</ZipTestArchiveTfm>
    </PropertyGroup>

    <MakeDir Directories="$(TestArchiveTestsDir)" />
    <ZipDirectory SourceDirectory="$(TestsArchiveSourceDir)"
                  DestinationFile="$([MSBuild]::NormalizePath($(TestArchiveTestsDir), '$(MSBuildProjectName)$(ZipTestArchiveTfm).zip'))"
                  Overwrite="true" />
  </Target>

  <!-- ENHANCED: ExtractTestClassNames now discovers collections -->
  <Target Name="ExtractTestClassNames"
          Condition=" '$(IsTestProject)' == 'true' and '$(SplitTestsForCI)' == 'true' and '$(PrepareForHelix)' == 'true' and '$(IsTestUtilityProject)' != 'true'"
          BeforeTargets="ZipTestArchive">

    <Error Condition="'$(TestClassNamesPrefix)' == ''"
           Text="%24(TestClassNamesPrefix) must be set when SplitTestsForCI=true. Example: -p:TestClassNamesPrefix=Aspire.Hosting.Tests" />

    <!-- Run test assembly to enumerate all tests with collection information -->
    <Exec Command="&quot;$(RunCommand)&quot; --filter-not-trait category=failing --list-tests-with-traits" ConsoleToMSBuild="true">
      <Output TaskParameter="ConsoleOutput" ItemName="_ListOfTestsLinesWithTraits" />
    </Exec>

    <PropertyGroup>
      <!-- Regex to extract collection names from xunit output -->
      <!-- xunit output format: "  Collection: CollectionName" -->
      <_CollectionRegex>^\s*Collection:\s*(.+)$</_CollectionRegex>
      
      <!-- Regex to extract test class names -->
      <_ClassRegex>^\s*($(TestClassNamesPrefix)[^\($]+)</_ClassRegex>
    </PropertyGroup>

    <!-- Extract collection names from test output -->
    <ItemGroup>
      <_CollectionLines Include="$([System.Text.RegularExpressions.Regex]::Match('%(_ListOfTestsLinesWithTraits.Identity)', '$(_CollectionRegex)'))" />
      <_CollectionNames Include="$([System.Text.RegularExpressions.Regex]::Match('%(_CollectionLines.Identity)', '$(_CollectionRegex)').Groups[1].Value)" 
                        Condition="'$([System.Text.RegularExpressions.Regex]::Match('%(_CollectionLines.Identity)', '$(_CollectionRegex)').Success)' == 'true'" />
    </ItemGroup>

    <!-- Get unique collection names and filter out excluded ones -->
    <ItemGroup>
      <UniqueCollections Include="@(_CollectionNames->Distinct())" Exclude="$(TestCollectionsToSkipSplitting)" />
    </ItemGroup>

    <PropertyGroup>
      <_HasCollections>false</_HasCollections>
      <_HasCollections Condition="'@(UniqueCollections->Count())' != '0'">true</_HasCollections>
    </PropertyGroup>

    <!-- Write the test list file -->
    <ItemGroup>
      <!-- Collections get their own entries -->
      <_TestListLines Include="collection:%(UniqueCollections.Identity)" />
      
      <!-- Add uncollected entry (always present to catch tests without collections) -->
      <_TestListLines Include="uncollected:*" />
    </ItemGroup>

    <WriteLinesToFile File="$(TestArchiveTestsDir)$(MSBuildProjectName).tests.list"
                      Lines="@(_TestListLines)"
                      Overwrite="true" />

    <!-- Write metadata for matrix generation -->
    <PropertyGroup>
      <!-- Normalize path separators for cross-platform compatibility -->
      <_RelativeProjectPath>$(MSBuildProjectDirectory.Replace('$(RepoRoot)', ''))</_RelativeProjectPath>
      <_RelativeProjectPath>$(_RelativeProjectPath.Replace('\', '/'))</_RelativeProjectPath>
      
      <!-- Build list of collection names for uncollected filter -->
      <_CollectionsList>@(UniqueCollections, ';')</_CollectionsList>
    </PropertyGroup>

    <ItemGroup>
      <_MetadataLines Include="{" />
      <_MetadataLines Include="  &quot;projectName&quot;: &quot;$(MSBuildProjectName)&quot;," />
      <_MetadataLines Include="  &quot;testClassNamesPrefix&quot;: &quot;$(TestClassNamesPrefix)&quot;," />
      <_MetadataLines Include="  &quot;testProjectPath&quot;: &quot;$(_RelativeProjectPath)/$(MSBuildProjectFile)&quot;," />
      <_MetadataLines Include="  &quot;collections&quot;: &quot;$(_CollectionsList)&quot;," />
      <_MetadataLines Include="  &quot;requiresNugets&quot;: &quot;$(RequiresNugetsForSplitTests)&quot;," />
      <_MetadataLines Include="  &quot;requiresTestSdk&quot;: &quot;$(RequiresTestSdkForSplitTests)&quot;," />
      <_MetadataLines Include="  &quot;testSessionTimeout&quot;: &quot;$(SplitTestSessionTimeout)&quot;," />
      <_MetadataLines Include="  &quot;testHangTimeout&quot;: &quot;$(SplitTestHangTimeout)&quot;," />
      <_MetadataLines Include="  &quot;uncollectedTestsSessionTimeout&quot;: &quot;$(UncollectedTestsSessionTimeout)&quot;," />
      <_MetadataLines Include="  &quot;uncollectedTestsHangTimeout&quot;: &quot;$(UncollectedTestsHangTimeout)&quot;," />
      <_MetadataLines Include="  &quot;enablePlaywrightInstall&quot;: &quot;$(EnablePlaywrightInstallForSplitTests)&quot;" />
      <_MetadataLines Include="}" />
    </ItemGroup>

    <WriteLinesToFile File="$(TestArchiveTestsDir)$(MSBuildProjectName).tests.metadata.json"
                      Lines="@(_MetadataLines)"
                      Overwrite="true" />

    <Message Text="[$(MSBuildProjectName)] Discovered @(UniqueCollections->Count()) collection(s)" 
             Importance="High" />
    <Message Text="[$(MSBuildProjectName)] Collections: $(_CollectionsList)" 
             Importance="High" 
             Condition="'@(UniqueCollections->Count())' != '0'" />
  </Target>

  <!-- MODIFIED: Update GetRunTestsOnGithubActions to include SplitTests metadata -->
  <Target Name="GetRunTestsOnGithubActions" Returns="@(TestProject)">
    <ItemGroup>
      <TestProject Condition="'$(BuildOs)' == 'windows'" 
                   Include="$(MSBuildProjectFullPath)" 
                   RunTestsOnGithubActions="$(RunOnGithubActionsWindows)" 
                   SplitTests="$(SplitTestsForCI)" />
      <TestProject Condition="'$(BuildOs)' == 'linux'" 
                   Include="$(MSBuildProjectFullPath)" 
                   RunTestsOnGithubActions="$(RunOnGithubActionsLinux)" 
                   SplitTests="$(SplitTestsForCI)" />
      <TestProject Condition="'$(BuildOs)' == 'darwin'" 
                   Include="$(MSBuildProjectFullPath)" 
                   RunTestsOnGithubActions="$(RunOnGithubActionsMacOS)" 
                   SplitTests="$(SplitTestsForCI)" />
    </ItemGroup>
  </Target>

  <!-- KEEP EXISTING IMPORTS -->
  <Import Project="$(TestsSharedDir)Aspire.Templates.Testing.targets" Condition="'$(IsTemplateTestProject)' == 'true'" />
  <Import Project="$(RepositoryEngineeringDir)Testing.targets" />
</Project>
```

## Key Changes from v1

### 1. Collection Discovery

```xml
<!-- Uses --list-tests-with-traits to get collection information -->
<Exec Command="&quot;$(RunCommand)&quot; --filter-not-trait category=failing --list-tests-with-traits" ConsoleToMSBuild="true">
  <Output TaskParameter="ConsoleOutput" ItemName="_ListOfTestsLinesWithTraits" />
</Exec>
```

### 2. Collection Extraction

```xml
<!-- Extract unique collection names -->
<ItemGroup>
  <UniqueCollections Include="@(_CollectionNames->Distinct())" Exclude="$(TestCollectionsToSkipSplitting)" />
</ItemGroup>
```

### 3. Simplified Test List Format

```xml
<!-- Output format: type:name -->
<ItemGroup>
  <_TestListLines Include="collection:%(UniqueCollections.Identity)" />
  <_TestListLines Include="uncollected:*" />
</ItemGroup>
```

### 4. Collection Metadata

```xml
<!-- Store collection list for uncollected filter generation -->
<_MetadataLines Include="  &quot;collections&quot;: &quot;$(_CollectionsList)&quot;," />
```

## Testing the MSBuild Changes

### Test 1: Project with No Collections

```bash
# Create a test project without collections
dotnet build tests/SomeProject.Tests/SomeProject.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsForCI=true \
  -p:TestClassNamesPrefix=SomeProject.Tests
```

**Expected `.tests.list` output**:
```
uncollected:*
```

**Expected matrix**: 1 job (UncollectedTests)

### Test 2: Project with Collections

Add collections to test classes:

```csharp
[Collection("DatabaseTests")]
public class PostgresTests { }

[Collection("DatabaseTests")]
public class MySqlTests { }

[Collection("ContainerTests")]
public class DockerTests { }

public class QuickTests { }  // No collection
```

Build:
```bash
dotnet build tests/SomeProject.Tests/SomeProject.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsForCI=true \
  -p:TestClassNamesPrefix=SomeProject.Tests
```

**Expected `.tests.list` output**:
```
collection:DatabaseTests
collection:ContainerTests
uncollected:*
```

**Expected matrix**: 3 jobs
1. Collection_DatabaseTests
2. Collection_ContainerTests
3. UncollectedTests

### Test 3: Exclude Collections

```bash
dotnet build tests/SomeProject.Tests/SomeProject.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsForCI=true \
  -p:TestClassNamesPrefix=SomeProject.Tests \
  -p:TestCollectionsToSkipSplitting=DatabaseTests
```

**Expected `.tests.list` output**:
```
collection:ContainerTests
uncollected:*
```

**Expected matrix**: 2 jobs
1. Collection_ContainerTests
2. UncollectedTests (includes DatabaseTests now)

## File: `tests/Shared/GetTestProjects.proj`

No changes needed from v1 - this file just orchestrates the builds and calls the PowerShell script.

## Next Steps

Proceed to [Step 2: PowerShell Script (v2)](./STEP_02_POWERSHELL_SCRIPT_V2.md)