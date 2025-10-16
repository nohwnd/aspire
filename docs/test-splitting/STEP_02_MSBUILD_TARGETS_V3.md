# Step 2: MSBuild Targets Implementation (v3 - Auto-Detection)

## Overview

Enhanced MSBuild targets that use the PowerShell discovery helper to automatically detect whether to use collection-based or class-based splitting.

## File: `tests/Directory.Build.targets`

### Complete Enhanced Implementation

```xml
<!-- Location: tests/Directory.Build.targets -->

<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" />
  <Import Project="$(TestsSharedRepoTestingDir)Aspire.RepoTesting.targets" />

  <PropertyGroup>
    <!-- Use a separate xunit.runner.json for helix that disables parallel test runs -->
    <XunitRunnerJson Condition="'$(XunitRunnerJson)' == '' and '$(PrepareForHelix)' == 'true'">$(RepoRoot)tests\helix\xunit.runner.json</XunitRunnerJson>
    <XunitRunnerJson Condition="'$(XunitRunnerJson)' == ''">$(RepositoryEngineeringDir)testing\xunit.runner.json</XunitRunnerJson>

    <!-- Properties to allow control tests to run, useful for local command line runs -->
    <TestingPlatformCommandLineArguments Condition="'$(TestMethod)' != ''">$(TestingPlatformCommandLineArguments) --filter-method $(TestMethod)</TestingPlatformCommandLineArguments>
    <TestingPlatformCommandLineArguments Condition="'$(TestClass)' != ''">$(TestingPlatformCommandLineArguments) --filter-class $(TestClass)</TestingPlatformCommandLineArguments>
    <TestingPlatformCommandLineArguments Condition="'$(TestNamespace)' != ''">$(TestingPlatformCommandLineArguments) --filter-namespace $(TestNamespace)</TestingPlatformCommandLineArguments>

    <TestCaptureOutput Condition="'$(TestCaptureOutput)' == '' and '$(ContinuousIntegrationBuild)' == 'true'">true</TestCaptureOutput>
    <!-- don't capture on local runs -->
    <TestCaptureOutput Condition="'$(TestCaptureOutput)' == ''">false</TestCaptureOutput>
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

  <!-- NEW: ExtractTestClassNames with auto-detection -->
  <Target Name="ExtractTestClassNames"
          Condition=" '$(IsTestProject)' == 'true' and '$(SplitTestsOnCI)' == 'true' and '$(PrepareForHelix)' == 'true' and '$(IsTestUtilityProject)' != 'true'"
          BeforeTargets="ZipTestArchive">

    <Error Condition="'$(TestClassNamesPrefix)' == ''"
           Text="%24(TestClassNamesPrefix) must be set when SplitTestsOnCI=true. Example: -p:TestClassNamesPrefix=Aspire.Hosting.Tests" />

    <Message Text="[$(MSBuildProjectName)] Starting test metadata extraction..." Importance="High" />

    <!-- Run test assembly to list all tests -->
    <Exec Command="&quot;$(RunCommand)&quot; --filter-not-trait category=failing --list-tests" 
          ConsoleToMSBuild="true"
          IgnoreExitCode="false">
      <Output TaskParameter="ConsoleOutput" ItemName="_TestAssemblyOutput" />
    </Exec>

    <PropertyGroup>
      <!-- Path to discovery helper script -->
      <_DiscoveryScriptPath>$(RepoRoot)eng\scripts\extract-test-metadata.ps1</_DiscoveryScriptPath>
      
      <!-- Output files -->
      <_TestListFile>$(TestArchiveTestsDir)$(MSBuildProjectName).tests.list</_TestListFile>
      <_MetadataFile>$(TestArchiveTestsDir)$(MSBuildProjectName).tests.metadata.json</_MetadataFile>
      
      <!-- Normalize path separators for cross-platform compatibility -->
      <_RelativeProjectPath>$(MSBuildProjectDirectory.Replace('$(RepoRoot)', ''))</_RelativeProjectPath>
      <_RelativeProjectPath>$(_RelativeProjectPath.Replace('\', '/'))</_RelativeProjectPath>
      
      <!-- Collections to skip (if any) -->
      <_CollectionsToSkip Condition="'$(TestCollectionsToSkipSplitting)' != ''">$(TestCollectionsToSkipSplitting)</_CollectionsToSkip>
      <_CollectionsToSkip Condition="'$(TestCollectionsToSkipSplitting)' == ''"></_CollectionsToSkip>
    </PropertyGroup>

    <!-- Ensure output directory exists -->
    <MakeDir Directories="$(TestArchiveTestsDir)" />

    <!-- Create temporary file with test assembly output -->
    <PropertyGroup>
      <_TempOutputFile>$(TestArchiveTestsDir)$(MSBuildProjectName).tests.output.tmp</_TempOutputFile>
    </PropertyGroup>

    <WriteLinesToFile File="$(_TempOutputFile)"
                      Lines="@(_TestAssemblyOutput)"
                      Overwrite="true" />

    <!-- Call PowerShell discovery helper -->
    <Message Text="[$(MSBuildProjectName)] Running discovery helper..." Importance="High" />
    
    <PropertyGroup>
      <_DiscoveryCommand>pwsh -NoProfile -ExecutionPolicy Bypass -File &quot;$(_DiscoveryScriptPath)&quot;</_DiscoveryCommand>
      <_DiscoveryCommand>$(_DiscoveryCommand) -TestAssemblyOutput (Get-Content '$(_TempOutputFile)')</_DiscoveryCommand>
      <_DiscoveryCommand>$(_DiscoveryCommand) -TestClassNamesPrefix &quot;$(TestClassNamesPrefix)&quot;</_DiscoveryCommand>
      <_DiscoveryCommand Condition="'$(_CollectionsToSkip)' != ''">$(_DiscoveryCommand) -TestCollectionsToSkip &quot;$(_CollectionsToSkip)&quot;</_DiscoveryCommand>
      <_DiscoveryCommand>$(_DiscoveryCommand) -OutputListFile &quot;$(_TestListFile)&quot;</_DiscoveryCommand>
    </PropertyGroup>

    <Exec Command="$(_DiscoveryCommand)" 
          IgnoreExitCode="false" 
          WorkingDirectory="$(RepoRoot)" />

    <!-- Clean up temp file -->
    <Delete Files="$(_TempOutputFile)" />

    <!-- Verify output file was created -->
    <Error Condition="!Exists('$(_TestListFile)')"
           Text="Discovery helper failed to generate test list file: $(_TestListFile)" />

    <!-- Read the generated file to detect mode -->
    <ReadLinesFromFile File="$(_TestListFile)">
      <Output TaskParameter="Lines" ItemName="_GeneratedListLines" />
    </ReadLinesFromFile>

    <PropertyGroup>
      <!-- Detect mode from first line -->
      <_FirstLine>@(_GeneratedListLines->WithMetadataValue('Identity', '@(_GeneratedListLines, 0)'))</_FirstLine>
      <_DetectedMode Condition="$(_FirstLine.StartsWith('collection:'))">collection</_DetectedMode>
      <_DetectedMode Condition="$(_FirstLine.StartsWith('class:'))">class</_DetectedMode>
      
      <!-- Count entries -->
      <_EntryCount>@(_GeneratedListLines->Count())</_EntryCount>
    </PropertyGroup>

    <Message Text="[$(MSBuildProjectName)] Detected mode: $(_DetectedMode)" Importance="High" />
    <Message Text="[$(MSBuildProjectName)] Generated entries: $(_EntryCount)" Importance="High" />

    <!-- Parse collections list if in collection mode -->
    <PropertyGroup Condition="'$(_DetectedMode)' == 'collection'">
      <_CollectionsList></_CollectionsList>
    </PropertyGroup>

    <ItemGroup Condition="'$(_DetectedMode)' == 'collection'">
      <_CollectionLines Include="@(_GeneratedListLines)" Condition="$([System.String]::Copy('%(Identity)').StartsWith('collection:'))" />
      <_CollectionNames Include="$([System.String]::Copy('%(_CollectionLines.Identity)').Substring(11))" />
    </ItemGroup>

    <PropertyGroup Condition="'$(_DetectedMode)' == 'collection'">
      <_CollectionsList>@(_CollectionNames, ';')</_CollectionsList>
    </PropertyGroup>

    <!-- Write metadata file -->
    <ItemGroup>
      <_MetadataLines Include="{" />
      <_MetadataLines Include="  &quot;projectName&quot;: &quot;$(MSBuildProjectName)&quot;," />
      <_MetadataLines Include="  &quot;testClassNamesPrefix&quot;: &quot;$(TestClassNamesPrefix)&quot;," />
      <_MetadataLines Include="  &quot;testProjectPath&quot;: &quot;$(_RelativeProjectPath)/$(MSBuildProjectFile)&quot;," />
      <_MetadataLines Include="  &quot;mode&quot;: &quot;$(_DetectedMode)&quot;," />
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

    <WriteLinesToFile File="$(_MetadataFile)"
                      Lines="@(_MetadataLines)"
                      Overwrite="true" />

    <Message Text="[$(MSBuildProjectName)] ✅ Test metadata extraction complete!" Importance="High" />
    <Message Text="[$(MSBuildProjectName)] Mode: $(_DetectedMode) | Entries: $(_EntryCount)" Importance="High" />
    <Message Text="[$(MSBuildProjectName)] Files: $(_TestListFile), $(_MetadataFile)" Importance="High" />
  </Target>

  <!-- MODIFIED: Update GetRunTestsOnGithubActions to include SplitTests metadata -->
  <Target Name="GetRunTestsOnGithubActions" Returns="@(TestProject)">
    <ItemGroup>
      <TestProject Condition="'$(BuildOs)' == 'windows'" 
                   Include="$(MSBuildProjectFullPath)" 
                   RunTestsOnGithubActions="$(RunOnGithubActionsWindows)" 
                   SplitTests="$(SplitTestsOnCI)" />
      <TestProject Condition="'$(BuildOs)' == 'linux'" 
                   Include="$(MSBuildProjectFullPath)" 
                   RunTestsOnGithubActions="$(RunOnGithubActionsLinux)" 
                   SplitTests="$(SplitTestsOnCI)" />
      <TestProject Condition="'$(BuildOs)' == 'darwin'" 
                   Include="$(MSBuildProjectFullPath)" 
                   RunTestsOnGithubActions="$(RunOnGithubActionsMacOS)" 
                   SplitTests="$(SplitTestsOnCI)" />
    </ItemGroup>
  </Target>

  <!-- KEEP EXISTING IMPORTS -->
  <Import Project="$(TestsSharedDir)Aspire.Templates.Testing.targets" Condition="'$(IsTemplateTestProject)' == 'true'" />
  <Import Project="$(RepositoryEngineeringDir)Testing.targets" />
</Project>
```

## Key Features

### 1. PowerShell Helper Integration

```xml
<!-- Run PowerShell discovery helper with test assembly output -->
<Exec Command="$(_DiscoveryCommand)" 
      IgnoreExitCode="false" 
      WorkingDirectory="$(RepoRoot)" />
```

The command passes:
- Test assembly output (--list-tests results)
- Test class prefix for filtering
- Collections to skip (optional)
- Output file path

### 2. Automatic Mode Detection

```xml
<!-- Detect mode from first line of generated file -->
<PropertyGroup>
  <_DetectedMode Condition="$(_FirstLine.StartsWith('collection:'))">collection</_DetectedMode>
  <_DetectedMode Condition="$(_FirstLine.StartsWith('class:'))">class</_DetectedMode>
</PropertyGroup>
```

### 3. Metadata Generation

The metadata file includes the detected mode:

```json
{
  "mode": "collection",  // or "class"
  "collections": "DatabaseTests;ContainerTests",
  ...
}
```

## Testing the MSBuild Target

### Test 1: Project with Collections

Create a test project with collections:

```csharp
// Aspire.Hosting.Tests/DatabaseTests.cs
[Collection("DatabaseTests")]
public class PostgresTests { }

[Collection("DatabaseTests")]
public class MySqlTests { }

// Aspire.Hosting.Tests/QuickTests.cs
public class QuickTests { }  // No collection
```

Build:

```bash
dotnet build tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsOnCI=true \
  -p:TestClassNamesPrefix=Aspire.Hosting.Tests \
  /bl:test.binlog
```

**Expected Console Output**:
```
[Aspire.Hosting.Tests] Starting test metadata extraction...
[Aspire.Hosting.Tests] Running discovery helper...
ℹ️ Parsing test assembly output...
🔍   Found collection: DatabaseTests
✅ Detection Results:
ℹ️   Mode: collection
ℹ️   Collections found: 1
...
[Aspire.Hosting.Tests] Detected mode: collection
[Aspire.Hosting.Tests] Generated entries: 2
[Aspire.Hosting.Tests] ✅ Test metadata extraction complete!
```

**Check Output Files**:

```bash
# List file
cat artifacts/helix/Aspire.Hosting.Tests.tests.list
# collection:DatabaseTests
# uncollected:*

# Metadata file
cat artifacts/helix/Aspire.Hosting.Tests.tests.metadata.json | jq .mode
# "collection"
```

### Test 2: Project without Collections

```csharp
// Aspire.Templates.Tests/Test1.cs
public class Test1 { }

// Aspire.Templates.Tests/Test2.cs
public class Test2 { }
```

Build:

```bash
dotnet build tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsOnCI=true \
  -p:TestClassNamesPrefix=Aspire.Templates.Tests
```

**Expected Console Output**:
```
[Aspire.Templates.Tests] Starting test metadata extraction...
[Aspire.Templates.Tests] Running discovery helper...
ℹ️ Parsing test assembly output...
✅ Detection Results:
ℹ️   Mode: class
ℹ️   Test classes found: 12
...
[Aspire.Templates.Tests] Detected mode: class
[Aspire.Templates.Tests] Generated entries: 12
[Aspire.Templates.Tests] ✅ Test metadata extraction complete!
```

**Check Output Files**:

```bash
# List file
cat artifacts/helix/Aspire.Templates.Tests.tests.list
# class:Aspire.Templates.Tests.Test1
# class:Aspire.Templates.Tests.Test2
# ...

# Metadata file
cat artifacts/helix/Aspire.Templates.Tests.tests.metadata.json | jq .mode
# "class"
```

### Test 3: With Skipped Collections

```bash
dotnet build tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj \
  /t:Build;ExtractTestClassNames \
  -p:PrepareForHelix=true \
  -p:SplitTestsOnCI=true \
  -p:TestClassNamesPrefix=Aspire.Hosting.Tests \
  -p:TestCollectionsToSkipSplitting="QuickTests;FastTests"
```

**Result**: QuickTests and FastTests won't appear in collection list; they'll run in uncollected job.

## Debugging

### View Binlog

```bash
# Install dotnet-binlog if not already installed
dotnet tool install -g dotnet-binlog

# View the binlog
dotnet-binlog test.binlog
```

Look for:
- ExtractTestClassNames target execution
- Console output from test assembly
- PowerShell script execution
- Generated file contents

### Common Issues

#### Issue 1: "Discovery helper failed"

**Symptom**: Target fails with error about missing output file  
**Cause**: PowerShell script errored  
**Fix**: Check script output in binlog; may need to update regex patterns

#### Issue 2: "No tests found"

**Symptom**: Empty .tests.list file  
**Cause**: TestClassNamesPrefix doesn't match test namespace  
**Fix**: Verify prefix matches actual test namespace

#### Issue 3: "Mode is empty"

**Symptom**: `$(_DetectedMode)` is blank  
**Cause**: Generated file has unexpected format  
**Fix**: Check .tests.list file content manually

### Manual Verification

```bash
# Check generated files
ls -la artifacts/helix/*.tests.list
ls -la artifacts/helix/*.tests.metadata.json

# View contents
cat artifacts/helix/YourProject.Tests.tests.list
cat artifacts/helix/YourProject.Tests.tests.metadata.json | jq .

# Verify mode detection
cat artifacts/helix/YourProject.Tests.tests.metadata.json | jq -r .mode
# Should output: "collection" or "class"
```

## File: `tests/Shared/GetTestProjects.proj`

### No Changes Needed

The existing v1 implementation works fine - it just calls MSBuild targets and then the PowerShell matrix generator.

```xml
<Project DefaultTargets="GenerateTestMatrices">
  <PropertyGroup>
    <RepoRoot>$(MSBuildThisFileDirectory)..\..\</RepoRoot>
    <TestMatrixOutputPath Condition="'$(TestMatrixOutputPath)' == ''">$(ArtifactsDir)test-matrices\</TestMatrixOutputPath>
  </PropertyGroup>

  <Target Name="GenerateTestMatrices" 
          DependsOnTargets="GenerateListOfTestsForGithubActions;GenerateSplitTestsMatrix">
  </Target>

  <Target Name="GenerateListOfTestsForGithubActions">
    <!-- Existing implementation from v1 - no changes needed -->
    <!-- ... -->
  </Target>

  <Target Name="GenerateSplitTestsMatrix">
    <!-- Existing implementation from v1 - calls PowerShell script -->
    <!-- ... -->
  </Target>
</Project>
```

## Next Steps

Proceed to [Step 3: Matrix Generator (v3)](./STEP_03_MATRIX_GENERATOR_V3.md)