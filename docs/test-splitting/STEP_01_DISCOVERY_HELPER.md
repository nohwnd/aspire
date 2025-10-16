# Step 1: PowerShell Discovery Helper

## Overview

Create a PowerShell helper script that parses `--list-tests` output to detect xUnit collections and test classes, determining the optimal splitting mode.

## File: `eng/scripts/extract-test-metadata.ps1`

### Complete Implementation

```powershell
<#
.SYNOPSIS
    Extracts test metadata (collections or classes) from xUnit test assembly.

.DESCRIPTION
    Parses output of 'dotnet test.dll --list-tests' to determine:
    - Are collections present? → Use collection-based splitting
    - No collections? → Use class-based splitting
    
    Outputs a structured list file for consumption by matrix generation.

.PARAMETER TestAssemblyOutput
    The console output from running the test assembly with --list-tests

.PARAMETER TestClassNamesPrefix
    Prefix to filter test classes (e.g., "Aspire.Hosting.Tests")

.PARAMETER TestCollectionsToSkip
    Semicolon-separated list of collection names to exclude from splitting

.PARAMETER OutputListFile
    Path to write the .tests.list file

.EXAMPLE
    $output = & dotnet MyTests.dll --list-tests
    .\extract-test-metadata.ps1 -TestAssemblyOutput $output -TestClassNamesPrefix "MyTests" -OutputListFile "./tests.list"

.NOTES
    Author: Aspire Team (@radical)
    Date: 2025-10-16
    Version: 3.0
    Requires: PowerShell 7.0+
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Output from test assembly --list-tests")]
    [string[]]$TestAssemblyOutput,

    [Parameter(Mandatory=$true, HelpMessage="Prefix for test class names")]
    [string]$TestClassNamesPrefix,

    [Parameter(Mandatory=$false, HelpMessage="Collections to skip (semicolon-separated)")]
    [string]$TestCollectionsToSkip = "",

    [Parameter(Mandatory=$true, HelpMessage="Output file path")]
    [string]$OutputListFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

#region Helper Functions

function Write-Message {
    param(
        [string]$Message,
        [ValidateSet('Info', 'Success', 'Warning', 'Error', 'Debug')]
        [string]$Level = 'Info'
    )
    
    $prefix = switch ($Level) {
        'Success' { '✅' }
        'Warning' { '⚠️' }
        'Error'   { '❌' }
        'Debug'   { '🔍' }
        default   { 'ℹ️' }
    }
    
    Write-Host "$prefix $Message"
}

#endregion

#region Parse Test Output

Write-Message "Parsing test assembly output..." -Level Info

# xUnit v3 output format when listing tests:
# The test assembly output includes test names with their collection information.
# We need to extract both collections and class names.

$collections = [System.Collections.Generic.HashSet[string]]::new()
$testClasses = [System.Collections.Generic.HashSet[string]]::new()

# Regex patterns
$testNameRegex = "^\s*($TestClassNamesPrefix[^\(]+)"
$collectionIndicator = "Collection:"  # xUnit prints this before test names in a collection

$currentCollection = $null

foreach ($line in $TestAssemblyOutput) {
    # Check if this line indicates a collection
    if ($line -match "^\s*$collectionIndicator\s*(.+)$") {
        $currentCollection = $Matches[1].Trim()
        Write-Message "  Found collection: $currentCollection" -Level Debug
        [void]$collections.Add($currentCollection)
        continue
    }
    
    # Check if this is a test name line
    if ($line -match $testNameRegex) {
        $fullTestName = $Matches[1].Trim()
        
        # Extract class name from test name
        # Format: "Namespace.ClassName.MethodName"
        if ($fullTestName -match "^($TestClassNamesPrefix\.[^\.]+)\.") {
            $className = $Matches[1]
            [void]$testClasses.Add($className)
        }
    }
}

#endregion

#region Filter Collections

$collectionsToSkipList = if ($TestCollectionsToSkip) {
    $TestCollectionsToSkip -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
} else {
    @()
}

$filteredCollections = $collections | Where-Object { $_ -notin $collectionsToSkipList }

#endregion

#region Determine Splitting Mode

$hasCollections = $filteredCollections.Count -gt 0
$mode = if ($hasCollections) { "collection" } else { "class" }

Write-Message "" -Level Info
Write-Message "Detection Results:" -Level Success
Write-Message "  Mode: $mode" -Level Info
Write-Message "  Collections found: $($collections.Count)" -Level Info
Write-Message "  Collections after filtering: $($filteredCollections.Count)" -Level Info
Write-Message "  Test classes found: $($testClasses.Count)" -Level Info

if ($collectionsToSkipList.Count -gt 0) {
    Write-Message "  Skipped collections: $($collectionsToSkipList -join ', ')" -Level Info
}

#endregion

#region Generate Output File

$outputLines = [System.Collections.Generic.List[string]]::new()

if ($mode -eq "collection") {
    Write-Message "" -Level Info
    Write-Message "Using COLLECTION-BASED splitting" -Level Success
    
    # Add collection entries
    foreach ($collection in ($filteredCollections | Sort-Object)) {
        $outputLines.Add("collection:$collection")
        Write-Message "  + Job: Collection_$collection" -Level Debug
    }
    
    # Always add uncollected entry
    $outputLines.Add("uncollected:*")
    Write-Message "  + Job: Uncollected (tests without collections)" -Level Debug
    
    Write-Message "" -Level Info
    Write-Message "Expected jobs: $($filteredCollections.Count + 1) ($($filteredCollections.Count) collections + 1 uncollected)" -Level Success
}
else {
    Write-Message "" -Level Info
    Write-Message "Using CLASS-BASED splitting" -Level Success
    
    # Add class entries
    foreach ($className in ($testClasses | Sort-Object)) {
        $outputLines.Add("class:$className")
        $shortName = $className -replace "^$TestClassNamesPrefix\.", ""
        Write-Message "  + Job: $shortName" -Level Debug
    }
    
    Write-Message "" -Level Info
    Write-Message "Expected jobs: $($testClasses.Count) (one per class)" -Level Success
}

#endregion

#region Write Output File

# Ensure output directory exists
$outputDir = [System.IO.Path]::GetDirectoryName($OutputListFile)
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Write file
$outputLines | Set-Content -Path $OutputListFile -Encoding UTF8

Write-Message "" -Level Info
Write-Message "Output written to: $OutputListFile" -Level Success
Write-Message "Lines: $($outputLines.Count)" -Level Info

#endregion
```

## Usage Examples

### Example 1: Project with Collections

```powershell
# Run test assembly
$output = & dotnet artifacts/bin/Aspire.Hosting.Tests/Debug/net9.0/Aspire.Hosting.Tests.dll --list-tests

# Extract metadata
.\eng\scripts\extract-test-metadata.ps1 `
    -TestAssemblyOutput $output `
    -TestClassNamesPrefix "Aspire.Hosting.Tests" `
    -OutputListFile "./artifacts/helix/Aspire.Hosting.Tests.tests.list"
```

**Console Output**:
```
ℹ️ Parsing test assembly output...
🔍   Found collection: DatabaseTests
🔍   Found collection: ContainerTests

✅ Detection Results:
ℹ️   Mode: collection
ℹ️   Collections found: 2
ℹ️   Collections after filtering: 2
ℹ️   Test classes found: 15

✅ Using COLLECTION-BASED splitting
🔍   + Job: Collection_DatabaseTests
🔍   + Job: Collection_ContainerTests
🔍   + Job: Uncollected (tests without collections)

✅ Expected jobs: 3 (2 collections + 1 uncollected)

✅ Output written to: ./artifacts/helix/Aspire.Hosting.Tests.tests.list
ℹ️ Lines: 3
```

**Output File** (`Aspire.Hosting.Tests.tests.list`):
```
collection:ContainerTests
collection:DatabaseTests
uncollected:*
```

### Example 2: Project without Collections

```powershell
$output = & dotnet artifacts/bin/Aspire.Templates.Tests/Debug/net9.0/Aspire.Templates.Tests.dll --list-tests

.\eng\scripts\extract-test-metadata.ps1 `
    -TestAssemblyOutput $output `
    -TestClassNamesPrefix "Aspire.Templates.Tests" `
    -OutputListFile "./artifacts/helix/Aspire.Templates.Tests.tests.list"
```

**Console Output**:
```
ℹ️ Parsing test assembly output...

✅ Detection Results:
ℹ️   Mode: class
ℹ️   Collections found: 0
ℹ️   Collections after filtering: 0
ℹ️   Test classes found: 12

✅ Using CLASS-BASED splitting
🔍   + Job: BuildAndRunStarterTemplateBuiltInTest
🔍   + Job: BuildAndRunTemplateTests
🔍   + Job: EmptyTemplateRunTests
...

✅ Expected jobs: 12 (one per class)

✅ Output written to: ./artifacts/helix/Aspire.Templates.Tests.tests.list
ℹ️ Lines: 12
```

**Output File** (`Aspire.Templates.Tests.tests.list`):
```
class:Aspire.Templates.Tests.BuildAndRunStarterTemplateBuiltInTest
class:Aspire.Templates.Tests.BuildAndRunTemplateTests
class:Aspire.Templates.Tests.EmptyTemplateRunTests
class:Aspire.Templates.Tests.MSTest_PerTestFrameworkTemplatesTests
class:Aspire.Templates.Tests.NewUpAndBuildStandaloneTemplateTests
class:Aspire.Templates.Tests.None_StarterTemplateProjectNamesTests
class:Aspire.Templates.Tests.Nunit_PerTestFrameworkTemplatesTests
class:Aspire.Templates.Tests.Nunit_StarterTemplateProjectNamesTests
class:Aspire.Templates.Tests.StarterTemplateRunTests
class:Aspire.Templates.Tests.StarterTemplateWithTestsRunTests
class:Aspire.Templates.Tests.Xunit_PerTestFrameworkTemplatesTests
class:Aspire.Templates.Tests.Xunit_StarterTemplateProjectNamesTests
```

### Example 3: Skip Certain Collections

```powershell
.\eng\scripts\extract-test-metadata.ps1 `
    -TestAssemblyOutput $output `
    -TestClassNamesPrefix "Aspire.Hosting.Tests" `
    -TestCollectionsToSkip "QuickTests;FastTests" `
    -OutputListFile "./artifacts/helix/Aspire.Hosting.Tests.tests.list"
```

**Result**: QuickTests and FastTests won't get their own jobs; they'll run in the uncollected job.

## Testing the Script

### Test 1: Mock Collection Output

```powershell
$mockOutput = @(
    "Collection: DatabaseTests",
    "  Aspire.Hosting.Tests.PostgresTests.CanStartContainer",
    "  Aspire.Hosting.Tests.PostgresTests.CanConnectToDatabase",
    "Collection: ContainerTests",
    "  Aspire.Hosting.Tests.DockerTests.CanStartGenericContainer",
    "Aspire.Hosting.Tests.QuickTests.FastTest1",
    "Aspire.Hosting.Tests.QuickTests.FastTest2"
)

.\eng\scripts\extract-test-metadata.ps1 `
    -TestAssemblyOutput $mockOutput `
    -TestClassNamesPrefix "Aspire.Hosting.Tests" `
    -OutputListFile "./test-output.list"
```

**Expected**:
- Mode: collection
- Collections: DatabaseTests, ContainerTests
- Output: 3 lines (2 collections + uncollected)

### Test 2: Mock Class-Only Output

```powershell
$mockOutput = @(
    "Aspire.Templates.Tests.Test1.Method1",
    "Aspire.Templates.Tests.Test1.Method2",
    "Aspire.Templates.Tests.Test2.Method1",
    "Aspire.Templates.Tests.Test3.Method1"
)

.\eng\scripts\extract-test-metadata.ps1 `
    -TestAssemblyOutput $mockOutput `
    -TestClassNamesPrefix "Aspire.Templates.Tests" `
    -OutputListFile "./test-output.list"
```

**Expected**:
- Mode: class
- Classes: Test1, Test2, Test3
- Output: 3 lines (one per class)

## Next Steps

Proceed to [Step 2: MSBuild Targets (v3)](./STEP_02_MSBUILD_TARGETS_V3.md)