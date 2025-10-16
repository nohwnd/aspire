# Step 1: MSBuild Targets Implementation

## Overview

Modify MSBuild targets to support unified test splitting mechanism while maintaining all 3 OS compatibility.

## File: `tests/Directory.Build.targets`

### Changes Required

1. **Add new ExtractTestClassNames target** (replacing existing)
2. **Add metadata generation**
3. **Update GetRunTestsOnGithubActions target**

### Implementation

```xml
<!-- Location: tests/Directory.Build.targets -->

<!-- Add after existing imports at top -->
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" />
  <Import Project="$(TestsSharedRepoTestingDir)Aspire.RepoTesting.targets" />

  <!-- EXISTING PropertyGroup for XunitRunnerJson, TestingPlatformCommandLineArguments, etc. -->
  <!-- KEEP AS IS -->

  <!-- MODIFIED: Enhanced ExtractTestClassNames target -->
  <Target Name="ExtractTestClassNames"
          Condition=" '$(IsTestProject)' == 'true' and '$(SplitTestsForCI)' == 'true' and '$(PrepareForHelix)' == 'true' and '$(IsTestUtilityProject)' != 'true'"
          BeforeTargets="ZipTestArchive">

    <Error Condition="'$(TestClassNamesPrefix)' == ''"
           Text="%24(TestClassNamesPrefix) must be set when SplitTestsForCI=true. Example: -p:TestClassNamesPrefix=Aspire.Hosting.Tests" />

    <!-- Run test assembly to enumerate all test classes -->
    <Exec Command="&quot;$(RunCommand)&quot; --filter-not-trait category=failing --list-tests" ConsoleToMSBuild="true">
      <Output TaskParameter="ConsoleOutput" ItemName="_ListOfTestsLines" />
    </Exec>

    <PropertyGroup>
      <_Regex>^\s*($(TestClassNamesPrefix)[^\($]+)</_Regex>
    </PropertyGroup>

    <!-- Extract test class names using regex -->
    <ItemGroup>
      <_TestLines0 Include="$([System.Text.RegularExpressions.Regex]::Match('%(_ListOfTestsLines.Identity)', '$(_Regex)'))" />
      <TestClassName Include="$([System.IO.Path]::GetFileNameWithoutExtension('%(_TestLines0.Identity)'))" />
    </ItemGroup>

    <!-- Filter out duplicates and excluded classes -->
    <ItemGroup>
      <UniqueTestClassNamesFiltered Include="@(TestClassName->Distinct())" Exclude="$(TestClassNamesToSkipTests)" />
    </ItemGroup>

    <Error Text="No test classes found matching prefix '$(TestClassNamesPrefix)'!" 
           Condition="'@(TestClassName)' == ''" />

    <!-- Write test class list -->
    <WriteLinesToFile File="$(TestArchiveTestsDir)$(MSBuildProjectName).tests.list"
                      Lines="@(UniqueTestClassNamesFiltered)"
                      Overwrite="true" />

    <!-- NEW: Write metadata for matrix generation -->
    <PropertyGroup>
      <!-- Normalize path separators for cross-platform compatibility -->
      <_RelativeProjectPath>$(MSBuildProjectDirectory.Replace('$(RepoRoot)', ''))</_RelativeProjectPath>
      <_RelativeProjectPath>$(_RelativeProjectPath.Replace('\', '/'))</_RelativeProjectPath>
    </PropertyGroup>

    <ItemGroup>
      <_MetadataLines Include="{" />
      <_MetadataLines Include="  &quot;projectName&quot;: &quot;$(MSBuildProjectName)&quot;," />
      <_MetadataLines Include="  &quot;testClassNamesPrefix&quot;: &quot;$(TestClassNamesPrefix)&quot;," />
      <_MetadataLines Include="  &quot;testProjectPath&quot;: &quot;$(_RelativeProjectPath)/$(MSBuildProjectFile)&quot;," />
      <_MetadataLines Include="  &quot;requiresNugets&quot;: &quot;$(RequiresNugetsForSplitTests)&quot;," />
      <_MetadataLines Include="  &quot;requiresTestSdk&quot;: &quot;$(RequiresTestSdkForSplitTests)&quot;," />
      <_MetadataLines Include="  &quot;testSessionTimeout&quot;: &quot;$(SplitTestSessionTimeout)&quot;," />
      <_MetadataLines Include="  &quot;testHangTimeout&quot;: &quot;$(SplitTestHangTimeout)&quot;," />
      <_MetadataLines Include="  &quot;enablePlaywrightInstall&quot;: &quot;$(EnablePlaywrightInstallForSplitTests)&quot;" />
      <_MetadataLines Include="}" />
    </ItemGroup>

    <WriteLinesToFile File="$(TestArchiveTestsDir)$(MSBuildProjectName).tests.metadata.json"
                      Lines="@(_MetadataLines)"
                      Overwrite="true" />

    <Message Text="Extracted $(MSBuildProjectName): @(UniqueTestClassNamesFiltered->Count()) test class(es)" 
             Importance="High" />
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

## File: `tests/Shared/GetTestProjects.proj`

### Complete Replacement

```xml
<Project DefaultTargets="GenerateTestMatrices">
  <!--
    Generates test project lists and matrices for CI.
    
    This runs on each OS (Linux, macOS, Windows) to generate OS-specific matrices.
    
    Outputs:
      - $(TestsListOutputPath) - Regular test projects (one job per project)
      - $(TestsListOutputPath).split-projects - Projects that need splitting
      - $(TestMatrixOutputPath)/split-tests-matrix.json - Split test matrix
  -->

  <PropertyGroup>
    <RepoRoot>$(MSBuildThisFileDirectory)..\..\</RepoRoot>
    <TestMatrixOutputPath Condition="'$(TestMatrixOutputPath)' == ''">$(ArtifactsDir)test-matrices\</TestMatrixOutputPath>
  </PropertyGroup>

  <Target Name="GenerateTestMatrices" 
          DependsOnTargets="GenerateListOfTestsForGithubActions;GenerateSplitTestsMatrix">
    <Message Text="Generated test matrices in $(TestMatrixOutputPath)" Importance="High" />
  </Target>

  <Target Name="GenerateListOfTestsForGithubActions">
    <Error Condition="'$(TestsListOutputPath)' == ''"
           Text="%24(TestsListOutputPath) must be set" />

    <ItemGroup>
      <!-- Exclude utility and special test projects -->
      <_TestProjectsToExclude Include="$(RepoRoot)tests\Shared\**\*Tests.csproj" />
      <_TestProjectsToExclude Include="$(RepoRoot)tests\testproject\**\*Tests.csproj" />
      <_TestProjectsToExclude Include="$(RepoRoot)tests\TestingAppHost1\**\*Tests.csproj" />
      
      <!-- EndToEnd tests have their own job -->
      <_TestProjectsToExclude Include="$(RepoRoot)tests\Aspire.EndToEnd.Tests\**\*Tests.csproj" />

      <_TestProjects Include="$(RepoRoot)tests\**\*Tests.csproj"
                     Exclude="@(_TestProjectsToExclude)" />
    </ItemGroup>

    <!-- Query all test projects for their metadata -->
    <MSBuild Projects="@(_TestProjects)" Targets="GetRunTestsOnGithubActions">
      <Output TaskParameter="TargetOutputs" ItemName="ProjectMetadata" />
    </MSBuild>

    <!-- Separate regular tests from split tests -->
    <ItemGroup>
      <RegularTestProjects Include="@(ProjectMetadata)" 
                           Condition="'%(RunTestsOnGithubActions)' == 'true' and '%(SplitTests)' != 'true'" />
      <SplitTestProjects Include="@(ProjectMetadata)" 
                         Condition="'%(RunTestsOnGithubActions)' == 'true' and '%(SplitTests)' == 'true'" />
      
      <!-- Generate shortnames (e.g., Aspire.Hosting.Tests → Hosting) -->
      <RegularTestProjects ShortName="$([System.IO.Path]::GetFileNameWithoutExtension('%(Identity)').Replace('Aspire.', '').Replace('.Tests', ''))" />
      <SplitTestProjects ShortName="$([System.IO.Path]::GetFileNameWithoutExtension('%(Identity)').Replace('Aspire.', '').Replace('.Tests', ''))" />
    </ItemGroup>

    <Error Condition="@(RegularTestProjects->Count()) == 0 and @(SplitTestProjects->Count()) == 0" 
           Text="No test projects found for BuildOs=$(BuildOs)" />

    <!-- Write outputs -->
    <WriteLinesToFile File="$(TestsListOutputPath)"
                      Lines="@(RegularTestProjects->'%(ShortName)')"
                      Overwrite="true" />
    
    <WriteLinesToFile File="$(TestsListOutputPath).split-projects"
                      Lines="@(SplitTestProjects->'%(ShortName)')"
                      Overwrite="true"
                      Condition="@(SplitTestProjects->Count()) > 0" />

    <Message Text="[$(BuildOs)] Regular tests: @(RegularTestProjects->Count())" Importance="High" />
    <Message Text="[$(BuildOs)] Split tests: @(SplitTestProjects->Count())" Importance="High" />
  </Target>

  <Target Name="GenerateSplitTestsMatrix">
    <PropertyGroup>
      <_GenerateMatrixScript>$(RepoRoot)eng\scripts\generate-test-matrix.ps1</_GenerateMatrixScript>
      <_TestListsDir>$(ArtifactsDir)helix\</_TestListsDir>
    </PropertyGroup>

    <MakeDir Directories="$(TestMatrixOutputPath)" />

    <!-- Only run if we have split test projects -->
    <Exec Command="pwsh -NoProfile -ExecutionPolicy Bypass -File &quot;$(_GenerateMatrixScript)&quot; -TestListsDirectory &quot;$(_TestListsDir)&quot; -OutputDirectory &quot;$(TestMatrixOutputPath)&quot; -BuildOs &quot;$(BuildOs)&quot;"
          Condition="Exists('$(TestsListOutputPath).split-projects')" 
          IgnoreExitCode="false" />
  </Target>
</Project>
```

## Testing the MSBuild Changes

### Local Testing

```bash
# On Linux/macOS
./build.sh -restore -build -projects tests/Shared/GetTestProjects.proj /p:TestsListOutputPath=$PWD/artifacts/test-list.txt /p:ContinuousIntegrationBuild=true

# On Windows
.\build.cmd -restore -build -projects tests/Shared/GetTestProjects.proj /p:TestsListOutputPath=%CD%\artifacts\test-list.txt /p:ContinuousIntegrationBuild=true
```

### Verify Outputs

Check these files were created:
- `artifacts/TestsForGithubActions.list` - Regular tests
- `artifacts/TestsForGithubActions.list.split-projects` - Projects to split (if any)

### Common Issues

1. **Path separators**: Ensure paths use `/` in JSON output
2. **Empty lists**: If no split projects, `.split-projects` file won't exist (this is OK)
3. **BuildOs detection**: Make sure `BuildOs` property is set correctly

## Next Steps

Proceed to [Step 2: PowerShell Script](./STEP_02_POWERSHELL_SCRIPT.md)