# Step 2: PowerShell Script Implementation (v2 - Collection Support)

## Overview

Enhanced PowerShell script that reads collection-based test lists and generates a matrix with:
- One entry per collection
- One entry for all uncollected tests

## File: `eng/scripts/generate-test-matrix.ps1`

### Complete Implementation

```powershell
<#
.SYNOPSIS
    Generates CI test matrices from collection-based test enumeration files.

.DESCRIPTION
    This script reads .tests.list and .tests.metadata.json files produced by the
    ExtractTestClassNames MSBuild target and generates a JSON matrix file for
    consumption by GitHub Actions or Azure DevOps.
    
    Supports both xUnit collections (grouped tests) and uncollected tests (catch-all).
    
    The script is cross-platform and runs on Windows, Linux, and macOS.

.PARAMETER TestListsDirectory
    Directory containing .tests.list and .tests.metadata.json files.
    Typically: artifacts/helix/

.PARAMETER OutputDirectory
    Directory where the JSON matrix file will be written.
    Typically: artifacts/test-matrices/

.PARAMETER BuildOs
    Current operating system being built for (windows, linux, darwin).
    Used for logging and debugging.

.EXAMPLE
    pwsh generate-test-matrix.ps1 -TestListsDirectory ./artifacts/helix -OutputDirectory ./artifacts/matrices -BuildOs linux

.NOTES
    Author: Aspire Team
    Date: 2025-10-16
    Version: 2.0 (Collection-based splitting)
    Requires: PowerShell 7.0+ (cross-platform)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, HelpMessage="Directory containing test list files")]
    [ValidateScript({Test-Path $_ -PathType Container})]
    [string]$TestListsDirectory,

    [Parameter(Mandatory=$true, HelpMessage="Output directory for matrix JSON")]
    [string]$OutputDirectory,

    [Parameter(Mandatory=$false, HelpMessage="Current OS: windows, linux, or darwin")]
    [ValidateSet('windows', 'linux', 'darwin', '')]
    [string]$BuildOs = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

#region Helper Functions

function Write-Message {
    <#
    .SYNOPSIS
        Writes a formatted message to the console.
    #>
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string]$Message,
        
        [Parameter(Mandatory=$false)]
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
    
    $color = switch ($Level) {
        'Success' { 'Green' }
        'Warning' { 'Yellow' }
        'Error'   { 'Red' }
        'Debug'   { 'Gray' }
        default   { 'Cyan' }
    }
    
    Write-Host "$prefix $Message" -ForegroundColor $color
}

function Get-TestListFiles {
    <#
    .SYNOPSIS
        Finds all .tests.list files in the specified directory.
    #>
    param([string]$Directory)
    
    Get-ChildItem -Path $Directory -Filter "*.tests.list" -Recurse -ErrorAction SilentlyContinue
}

function Read-TestMetadata {
    <#
    .SYNOPSIS
        Reads and parses test metadata JSON file.
    #>
    param(
        [string]$MetadataFile,
        [string]$ProjectName
    )
    
    # Default metadata values
    $defaults = @{
        testClassNamesPrefix = $ProjectName
        testProjectPath = "tests/$ProjectName/$ProjectName.csproj"
        collections = ''
        requiresNugets = 'false'
        requiresTestSdk = 'false'
        testSessionTimeout = '20m'
        testHangTimeout = '10m'
        uncollectedTestsSessionTimeout = '15m'
        uncollectedTestsHangTimeout = '8m'
        enablePlaywrightInstall = 'false'
    }
    
    if (-not (Test-Path $MetadataFile)) {
        Write-Message "No metadata file found for $ProjectName, using defaults" -Level Warning
        return $defaults
    }
    
    try {
        $content = Get-Content $MetadataFile -Raw | ConvertFrom-Json
        
        # Merge with defaults (content overrides defaults)
        foreach ($key in $content.PSObject.Properties.Name) {
            $defaults[$key] = $content.$key
        }
        
        return $defaults
    }
    catch {
        Write-Message "Failed to parse metadata for ${ProjectName}: $_" -Level Warning
        return $defaults
    }
}

function Get-CollectionFilterArg {
    <#
    .SYNOPSIS
        Generates xUnit filter argument for a specific collection.
    #>
    param([string]$CollectionName)
    
    return "--filter-collection `"$CollectionName`""
}

function Get-UncollectedFilterArg {
    <#
    .SYNOPSIS
        Generates xUnit filter argument to exclude all collections.
    #>
    param([string[]]$Collections)
    
    if ($Collections.Count -eq 0) {
        # No collections to exclude - run all tests
        return ""
    }
    
    # Build filter to exclude all collections
    $filters = $Collections | ForEach-Object { 
        "--filter-not-collection `"$_`"" 
    }
    
    return $filters -join ' '
}

function New-CollectionMatrixEntry {
    <#
    .SYNOPSIS
        Creates a matrix entry for a collection.
    #>
    param(
        [string]$CollectionName,
        [string]$ProjectName,
        [hashtable]$Metadata
    )
    
    $filterArg = Get-CollectionFilterArg -CollectionName $CollectionName
    
    # Check for per-collection timeout overrides
    $collectionTimeoutKey = "TestCollection_${CollectionName}_SessionTimeout"
    $collectionHangTimeoutKey = "TestCollection_${CollectionName}_HangTimeout"
    
    $sessionTimeout = $Metadata.testSessionTimeout
    $hangTimeout = $Metadata.testHangTimeout
    
    # Per-collection timeouts would come from metadata if specified
    # For now, use project defaults
    
    [ordered]@{
        type = "collection"
        name = $CollectionName
        shortname = "Collection_$CollectionName"
        projectName = $ProjectName
        testProjectPath = $Metadata.testProjectPath
        filterArg = $filterArg
        requiresNugets = ($Metadata.requiresNugets -eq 'true')
        requiresTestSdk = ($Metadata.requiresTestSdk -eq 'true')
        testSessionTimeout = $sessionTimeout
        testHangTimeout = $hangTimeout
        enablePlaywrightInstall = ($Metadata.enablePlaywrightInstall -eq 'true')
    }
}

function New-UncollectedMatrixEntry {
    <#
    .SYNOPSIS
        Creates a matrix entry for uncollected tests.
    #>
    param(
        [string[]]$Collections,
        [string]$ProjectName,
        [hashtable]$Metadata
    )
    
    $filterArg = Get-UncollectedFilterArg -Collections $Collections
    
    # Use specific timeouts for uncollected tests (usually faster)
    $sessionTimeout = if ($Metadata.uncollectedTestsSessionTimeout) { 
        $Metadata.uncollectedTestsSessionTimeout 
    } else { 
        $Metadata.testSessionTimeout 
    }
    
    $hangTimeout = if ($Metadata.uncollectedTestsHangTimeout) { 
        $Metadata.uncollectedTestsHangTimeout 
    } else { 
        $Metadata.testHangTimeout 
    }
    
    [ordered]@{
        type = "uncollected"
        name = "UncollectedTests"
        shortname = "Uncollected"
        projectName = $ProjectName
        testProjectPath = $Metadata.testProjectPath
        filterArg = $filterArg
        requiresNugets = ($Metadata.requiresNugets -eq 'true')
        requiresTestSdk = ($Metadata.requiresTestSdk -eq 'true')
        testSessionTimeout = $sessionTimeout
        testHangTimeout = $hangTimeout
        enablePlaywrightInstall = ($Metadata.enablePlaywrightInstall -eq 'true')
    }
}

function Parse-TestListFile {
    <#
    .SYNOPSIS
        Parses a .tests.list file and returns collections and flags.
    #>
    param([string]$FilePath)
    
    $lines = Get-Content $FilePath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    
    $result = @{
        Collections = [System.Collections.ArrayList]::new()
        HasUncollected = $false
    }
    
    foreach ($line in $lines) {
        if ($line -match '^collection:(.+)$') {
            [void]$result.Collections.Add($Matches[1].Trim())
        }
        elseif ($line -match '^uncollected:') {
            $result.HasUncollected = $true
        }
    }
    
    return $result
}

#endregion

#region Main Script

Write-Message "Starting collection-based matrix generation for BuildOs=$BuildOs"
Write-Message "Test lists directory: $TestListsDirectory"
Write-Message "Output directory: $OutputDirectory"

# Find all test list files
$listFiles = Get-TestListFiles -Directory $TestListsDirectory

if ($listFiles.Count -eq 0) {
    Write-Message "No test list files found in $TestListsDirectory" -Level Warning
    Write-Message "Creating empty matrix file..."
    
    # Create empty matrix
    $emptyMatrix = @{ include = @() }
    $outputFile = Join-Path $OutputDirectory "split-tests-matrix.json"
    
    # Ensure output directory exists
    if (-not (Test-Path $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }
    
    $emptyMatrix | ConvertTo-Json -Depth 10 -Compress | Set-Content -Path $outputFile -Encoding UTF8
    Write-Message "Created empty matrix: $outputFile" -Level Success
    exit 0
}

Write-Message "Found $($listFiles.Count) test list file(s)" -Level Success

# Process each test list file
$allEntries = [System.Collections.ArrayList]::new()
$stats = @{}

foreach ($listFile in $listFiles) {
    # Extract project name
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($listFile.Name -replace '\.tests$', '')
    
    Write-Message ""
    Write-Message "Processing $projectName..." -Level Info
    
    # Parse test list file
    $parsed = Parse-TestListFile -FilePath $listFile.FullName
    
    if ($parsed.Collections.Count -eq 0 -and -not $parsed.HasUncollected) {
        Write-Message "  No collections or uncollected tests found, skipping" -Level Warning
        continue
    }
    
    # Read metadata
    $metadataFile = $listFile.FullName -replace '\.tests\.list$', '.tests.metadata.json'
    $metadata = Read-TestMetadata -MetadataFile $metadataFile -ProjectName $projectName
    
    $projectStats = @{
        Collections = 0
        Uncollected = 0
    }
    
    # Generate matrix entries for each collection
    foreach ($collectionName in $parsed.Collections) {
        Write-Message "  Found collection: $collectionName" -Level Debug
        
        $entry = New-CollectionMatrixEntry `
            -CollectionName $collectionName `
            -ProjectName $projectName `
            -Metadata $metadata
        
        [void]$allEntries.Add($entry)
        $projectStats.Collections++
    }
    
    # Generate matrix entry for uncollected tests
    if ($parsed.HasUncollected) {
        Write-Message "  Adding uncollected tests job" -Level Debug
        
        $entry = New-UncollectedMatrixEntry `
            -Collections $parsed.Collections.ToArray() `
            -ProjectName $projectName `
            -Metadata $metadata
        
        [void]$allEntries.Add($entry)
        $projectStats.Uncollected = 1
    }
    
    $stats[$projectName] = $projectStats
    
    $totalJobs = $projectStats.Collections + $projectStats.Uncollected
    Write-Message "  ✅ Generated $totalJobs job(s): $($projectStats.Collections) collection(s) + $($projectStats.Uncollected) uncollected" -Level Success
}

# Generate final matrix
$matrix = @{
    include = $allEntries.ToArray()
}

# Write JSON file
$outputFile = Join-Path $OutputDirectory "split-tests-matrix.json"

# Ensure output directory exists
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$jsonOutput = $matrix | ConvertTo-Json -Depth 10 -Compress
$jsonOutput | Set-Content -Path $outputFile -Encoding UTF8 -NoNewline

# Summary
Write-Message ""
Write-Message ("=" * 60) -Level Info
Write-Message "Matrix Generation Summary" -Level Success
Write-Message ("=" * 60) -Level Info
Write-Message ""
Write-Message "Total Jobs: $($allEntries.Count)" -Level Success
Write-Message "Output File: $outputFile" -Level Success
Write-Message ""
Write-Message "Breakdown by Project:" -Level Info

foreach ($proj in $stats.Keys | Sort-Object) {
    $s = $stats[$proj]
    $collText = if ($s.Collections -eq 1) { "collection" } else { "collections" }
    $uncText = if ($s.Uncollected -eq 1) { "uncollected job" } else { "uncollected jobs" }
    
    Write-Message "  $proj`: $($s.Collections) $collText + $($s.Uncollected) $uncText" -Level Info
}

Write-Message ""
Write-Message "Matrix generation complete! ✨" -Level Success

#endregion
```

## Key Features of v2 Script

### 1. Collection Parsing

```powershell
function Parse-TestListFile {
    # Parses format:
    # collection:DatabaseTests
    # collection:ContainerTests
    # uncollected:*
    
    foreach ($line in $lines) {
        if ($line -match '^collection:(.+)$') {
            # Extract collection name
        }
        elseif ($line -match '^uncollected:') {
            # Flag that uncollected tests exist
        }
    }
}
```

### 2. Filter Generation

```powershell
# For a collection
"--filter-collection `"DatabaseTests`""

# For uncollected (exclude all collections)
"--filter-not-collection `"DatabaseTests`" --filter-not-collection `"ContainerTests`""
```

### 3. Smart Timeouts

```powershell
# Collections use project-level timeouts (usually longer)
$sessionTimeout = $Metadata.testSessionTimeout  # e.g., 25m

# Uncollected uses shorter timeouts (fast tests)
$sessionTimeout = $Metadata.uncollectedTestsSessionTimeout  # e.g., 15m
```

### 4. Matrix Entry Types

```powershell
# Collection entry
@{
    type = "collection"
    name = "DatabaseTests"
    filterArg = "--filter-collection `"DatabaseTests`""
    # ...
}

# Uncollected entry
@{
    type = "uncollected"
    name = "UncollectedTests"
    filterArg = "--filter-not-collection `"DatabaseTests`" ..."
    # ...
}
```

## Testing the Script

### Test 1: Project with No Collections

Create a test list file:

```bash
# artifacts/helix/SomeProject.Tests.tests.list
uncollected:*
```

Create metadata:

```json
{
  "projectName": "SomeProject.Tests",
  "testProjectPath": "tests/SomeProject.Tests/SomeProject.Tests.csproj",
  "collections": "",
  "testSessionTimeout": "20m",
  "testHangTimeout": "10m",
  "uncollectedTestsSessionTimeout": "15m",
  "uncollectedTestsHangTimeout": "8m"
}
```

Run script:

```powershell
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected Output**:
```json
{
  "include": [
    {
      "type": "uncollected",
      "name": "UncollectedTests",
      "shortname": "Uncollected",
      "projectName": "SomeProject.Tests",
      "testProjectPath": "tests/SomeProject.Tests/SomeProject.Tests.csproj",
      "filterArg": "",
      "requiresNugets": false,
      "requiresTestSdk": false,
      "testSessionTimeout": "15m",
      "testHangTimeout": "8m",
      "enablePlaywrightInstall": false
    }
  ]
}
```

**Result**: 1 job

### Test 2: Project with Collections

Create test list:

```bash
# artifacts/helix/Aspire.Hosting.Tests.tests.list
collection:DatabaseTests
collection:ContainerTests
uncollected:*
```

Create metadata:

```json
{
  "projectName": "Aspire.Hosting.Tests",
  "testProjectPath": "tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj",
  "collections": "DatabaseTests;ContainerTests",
  "testSessionTimeout": "25m",
  "testHangTimeout": "12m",
  "uncollectedTestsSessionTimeout": "15m",
  "uncollectedTestsHangTimeout": "8m",
  "requiresNugets": "false",
  "requiresTestSdk": "false",
  "enablePlaywrightInstall": "false"
}
```

Run script:

```powershell
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected Output**:

```json
{
  "include": [
    {
      "type": "collection",
      "name": "DatabaseTests",
      "shortname": "Collection_DatabaseTests",
      "projectName": "Aspire.Hosting.Tests",
      "filterArg": "--filter-collection \"DatabaseTests\"",
      "testSessionTimeout": "25m",
      "testHangTimeout": "12m",
      ...
    },
    {
      "type": "collection",
      "name": "ContainerTests",
      "shortname": "Collection_ContainerTests",
      "projectName": "Aspire.Hosting.Tests",
      "filterArg": "--filter-collection \"ContainerTests\"",
      "testSessionTimeout": "25m",
      "testHangTimeout": "12m",
      ...
    },
    {
      "type": "uncollected",
      "name": "UncollectedTests",
      "shortname": "Uncollected",
      "projectName": "Aspire.Hosting.Tests",
      "filterArg": "--filter-not-collection \"DatabaseTests\" --filter-not-collection \"ContainerTests\"",
      "testSessionTimeout": "15m",
      "testHangTimeout": "8m",
      ...
    }
  ]
}
```

**Result**: 3 jobs

### Test 3: Verify Filter Arguments

Load and inspect the matrix:

```powershell
$matrix = Get-Content ./artifacts/test-matrices/split-tests-matrix.json | ConvertFrom-Json

# Check collection filters
$matrix.include | Where-Object { $_.type -eq 'collection' } | ForEach-Object {
    Write-Host "$($_.name): $($_.filterArg)"
}

# Check uncollected filter
$uncollected = $matrix.include | Where-Object { $_.type -eq 'uncollected' }
Write-Host "Uncollected: $($uncollected.filterArg)"
```

**Expected Console Output**:
```
DatabaseTests: --filter-collection "DatabaseTests"
ContainerTests: --filter-collection "ContainerTests"
Uncollected: --filter-not-collection "DatabaseTests" --filter-not-collection "ContainerTests"
```

### Test 4: Multiple Projects

Create test lists for multiple projects:

```bash
# artifacts/helix/Aspire.Hosting.Tests.tests.list
collection:DatabaseTests
uncollected:*

# artifacts/helix/Aspire.Templates.Tests.tests.list
collection:StarterTemplate
collection:BasicTemplate
uncollected:*
```

Run script:

```powershell
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected Result**: 6 jobs total
- 2 from Aspire.Hosting.Tests (1 collection + 1 uncollected)
- 4 from Aspire.Templates.Tests (2 collections + 1 uncollected)

## Validation Checklist

- [ ] Script runs without errors on all 3 OSes
- [ ] Empty directory creates empty matrix
- [ ] Single uncollected entry creates 1 job
- [ ] Collections create separate jobs
- [ ] Uncollected filter excludes all collections
- [ ] Metadata defaults work when file missing
- [ ] JSON output is valid and parseable
- [ ] Filter arguments have correct syntax
- [ ] Timeouts are applied correctly
- [ ] Summary statistics are accurate

## Common Issues & Solutions

### Issue 1: "Collection not found" in test output

**Symptom**: xunit can't find collection name  
**Cause**: Collection name has special characters or spaces  
**Fix**: Escape collection names in filter arguments (already handled with quotes)

### Issue 2: Uncollected filter too long

**Symptom**: Command line too long with many collections  
**Cause**: Too many `--filter-not-collection` arguments  
**Fix**: Consider regrouping collections or using different approach

### Issue 3: Empty uncollected job

**Symptom**: Uncollected job runs but no tests execute  
**Cause**: All tests are in collections  
**Fix**: This is OK - job will exit with code 8 (zero tests), which we ignore

## Next Steps

Proceed to [Step 4: Project Configuration (v2)](./STEP_04_PROJECT_CONFIG_V2.md) - GitHub Actions doesn't need changes since it just consumes the matrix!