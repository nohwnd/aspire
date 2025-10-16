# Step 3: Matrix Generator Implementation (v3 - Dual Mode Support)

## Overview

Enhanced PowerShell script that reads the auto-detected test lists and generates matrices for both collection-based and class-based splitting modes.

## File: `eng/scripts/generate-test-matrix.ps1`

### Complete Implementation

```powershell
<#
.SYNOPSIS
    Generates CI test matrices from auto-detected test enumeration files.

.DESCRIPTION
    This script reads .tests.list and .tests.metadata.json files and generates
    a JSON matrix file for consumption by GitHub Actions or Azure DevOps.
    
    Automatically handles both modes:
    - Collection-based: Entries like "collection:Name" and "uncollected:*"
    - Class-based: Entries like "class:Full.Class.Name"
    
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
    Version: 3.0 (Auto-detection support)
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
        projectName = $ProjectName
        testClassNamesPrefix = $ProjectName
        testProjectPath = "tests/$ProjectName/$ProjectName.csproj"
        mode = 'class'
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

function Get-ClassFilterArg {
    <#
    .SYNOPSIS
        Generates xUnit filter argument for a specific test class.
    #>
    param([string]$ClassName)
    
    return "--filter-class `"$ClassName`""
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
    
    [ordered]@{
        type = "collection"
        name = $CollectionName
        shortname = "Collection_$CollectionName"
        projectName = $ProjectName
        testProjectPath = $Metadata.testProjectPath
        filterArg = $filterArg
        requiresNugets = ($Metadata.requiresNugets -eq 'true')
        requiresTestSdk = ($Metadata.requiresTestSdk -eq 'true')
        testSessionTimeout = $Metadata.testSessionTimeout
        testHangTimeout = $Metadata.testHangTimeout
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

function New-ClassMatrixEntry {
    <#
    .SYNOPSIS
        Creates a matrix entry for a test class.
    #>
    param(
        [string]$FullClassName,
        [string]$ProjectName,
        [hashtable]$Metadata
    )
    
    $prefix = $Metadata.testClassNamesPrefix
    $shortname = $FullClassName
    
    # Strip prefix if present (e.g., "Aspire.Templates.Tests.MyClass" → "MyClass")
    if ($prefix -and $FullClassName.StartsWith("$prefix.")) {
        $shortname = $FullClassName.Substring($prefix.Length + 1)
    }
    
    $filterArg = Get-ClassFilterArg -ClassName $FullClassName
    
    [ordered]@{
        type = "class"
        fullClassName = $FullClassName
        shortname = $shortname
        projectName = $ProjectName
        testProjectPath = $Metadata.testProjectPath
        filterArg = $filterArg
        requiresNugets = ($Metadata.requiresNugets -eq 'true')
        requiresTestSdk = ($Metadata.requiresTestSdk -eq 'true')
        testSessionTimeout = $Metadata.testSessionTimeout
        testHangTimeout = $Metadata.testHangTimeout
        enablePlaywrightInstall = ($Metadata.enablePlaywrightInstall -eq 'true')
    }
}

function Parse-TestListFile {
    <#
    .SYNOPSIS
        Parses a .tests.list file and returns structured data.
    #>
    param([string]$FilePath)
    
    $lines = Get-Content $FilePath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    
    $result = @{
        Mode = 'unknown'
        Collections = [System.Collections.ArrayList]::new()
        Classes = [System.Collections.ArrayList]::new()
        HasUncollected = $false
    }
    
    foreach ($line in $lines) {
        if ($line -match '^collection:(.+)$') {
            $result.Mode = 'collection'
            [void]$result.Collections.Add($Matches[1].Trim())
        }
        elseif ($line -match '^uncollected:') {
            $result.HasUncollected = $true
        }
        elseif ($line -match '^class:(.+)$') {
            $result.Mode = 'class'
            [void]$result.Classes.Add($Matches[1].Trim())
        }
    }
    
    return $result
}

#endregion

#region Main Script

Write-Message "Starting matrix generation for BuildOs=$BuildOs" -Level Success
Write-Message "Test lists directory: $TestListsDirectory"
Write-Message "Output directory: $OutputDirectory"
Write-Message ""

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
Write-Message ""

# Process each test list file
$allEntries = [System.Collections.ArrayList]::new()
$stats = @{}

foreach ($listFile in $listFiles) {
    # Extract project name
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($listFile.Name -replace '\.tests$', '')
    
    Write-Message "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -Level Info
    Write-Message "Processing: $projectName" -Level Info
    Write-Message "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -Level Info
    
    # Parse test list file
    $parsed = Parse-TestListFile -FilePath $listFile.FullName
    
    if ($parsed.Mode -eq 'unknown') {
        Write-Message "  Unable to determine mode, skipping" -Level Warning
        continue
    }
    
    # Read metadata
    $metadataFile = $listFile.FullName -replace '\.tests\.list$', '.tests.metadata.json'
    $metadata = Read-TestMetadata -MetadataFile $metadataFile -ProjectName $projectName
    
    Write-Message "  Mode: $($parsed.Mode)" -Level Info
    
    $projectStats = @{
        Mode = $parsed.Mode
        Collections = 0
        Classes = 0
        Uncollected = 0
    }
    
    if ($parsed.Mode -eq 'collection') {
        # Collection-based mode
        Write-Message "  Strategy: Collection-based splitting" -Level Success
        Write-Message ""
        
        # Generate matrix entries for each collection
        foreach ($collectionName in $parsed.Collections) {
            Write-Message "    ➕ Collection: $collectionName" -Level Debug
            
            $entry = New-CollectionMatrixEntry `
                -CollectionName $collectionName `
                -ProjectName $projectName `
                -Metadata $metadata
            
            [void]$allEntries.Add($entry)
            $projectStats.Collections++
        }
        
        # Generate matrix entry for uncollected tests
        if ($parsed.HasUncollected) {
            Write-Message "    ➕ Uncollected tests (all non-collection tests)" -Level Debug
            
            $entry = New-UncollectedMatrixEntry `
                -Collections $parsed.Collections.ToArray() `
                -ProjectName $projectName `
                -Metadata $metadata
            
            [void]$allEntries.Add($entry)
            $projectStats.Uncollected = 1
        }
        
        $totalJobs = $projectStats.Collections + $projectStats.Uncollected
        Write-Message ""
        Write-Message "  ✅ Generated $totalJobs job(s): $($projectStats.Collections) collection(s) + $($projectStats.Uncollected) uncollected" -Level Success
    }
    else {
        # Class-based mode
        Write-Message "  Strategy: Class-based splitting" -Level Success
        Write-Message ""
        
        # Generate matrix entries for each class
        foreach ($className in $parsed.Classes) {
            $shortName = $className -replace "^$($metadata.testClassNamesPrefix)\.", ""
            Write-Message "    ➕ Class: $shortName" -Level Debug
            
            $entry = New-ClassMatrixEntry `
                -FullClassName $className `
                -ProjectName $projectName `
                -Metadata $metadata
            
            [void]$allEntries.Add($entry)
            $projectStats.Classes++
        }
        
        Write-Message ""
        Write-Message "  ✅ Generated $($projectStats.Classes) job(s): one per class" -Level Success
    }
    
    $stats[$projectName] = $projectStats
    Write-Message ""
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
Write-Message "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -Level Info
Write-Message "Matrix Generation Summary" -Level Success
Write-Message "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -Level Info
Write-Message ""
Write-Message "Total Jobs: $($allEntries.Count)" -Level Success
Write-Message "Output File: $outputFile" -Level Success
Write-Message ""
Write-Message "Breakdown by Project:" -Level Info
Write-Message ""

foreach ($proj in $stats.Keys | Sort-Object) {
    $s = $stats[$proj]
    
    if ($s.Mode -eq 'collection') {
        $summary = "$($s.Collections) collection(s) + $($s.Uncollected) uncollected"
        Write-Message "  📦 $proj (collection mode): $summary" -Level Info
    }
    else {
        $summary = "$($s.Classes) class(es)"
        Write-Message "  📄 $proj (class mode): $summary" -Level Info
    }
}

Write-Message ""
Write-Message "Matrix generation complete! ✨" -Level Success

#endregion
```

## Key Features

### 1. Dual Mode Support

```powershell
if ($parsed.Mode -eq 'collection') {
    # Collection-based splitting
    # Generate: collection entries + uncollected entry
}
else {
    # Class-based splitting
    # Generate: one entry per class
}
```

### 2. Auto-Detection via File Parsing

```powershell
# Parse .tests.list file format
if ($line -match '^collection:(.+)$') {
    $result.Mode = 'collection'
    # ...
}
elseif ($line -match '^class:(.+)$') {
    $result.Mode = 'class'
    # ...
}
```

### 3. Unified Matrix Entry Creation

Each mode has its own entry creator:
- `New-CollectionMatrixEntry`: For collection jobs
- `New-UncollectedMatrixEntry`: For uncollected catch-all
- `New-ClassMatrixEntry`: For individual test classes

### 4. Rich Logging

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Processing: Aspire.Hosting.Tests
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Mode: collection
  Strategy: Collection-based splitting
  
    ➕ Collection: DatabaseTests
    ➕ Collection: ContainerTests
    ➕ Uncollected tests (all non-collection tests)
  
  ✅ Generated 3 job(s): 2 collection(s) + 1 uncollected
```

## Testing the Script

### Test 1: Collection Mode

Create test files:

```bash
# artifacts/helix/Aspire.Hosting.Tests.tests.list
collection:DatabaseTests
collection:ContainerTests
uncollected:*
```

```json
// artifacts/helix/Aspire.Hosting.Tests.tests.metadata.json
{
  "projectName": "Aspire.Hosting.Tests",
  "testProjectPath": "tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj",
  "mode": "collection",
  "collections": "DatabaseTests;ContainerTests",
  "testSessionTimeout": "25m",
  "testHangTimeout": "12m",
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

**Expected Console Output**:
```
✅ Starting matrix generation for BuildOs=linux
ℹ️ Test lists directory: ./artifacts/helix
ℹ️ Output directory: ./artifacts/test-matrices

✅ Found 1 test list file(s)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ℹ️ Processing: Aspire.Hosting.Tests
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ℹ️   Mode: collection
✅   Strategy: Collection-based splitting

🔍     ➕ Collection: DatabaseTests
🔍     ➕ Collection: ContainerTests
🔍     ➕ Uncollected tests (all non-collection tests)

✅   Generated 3 job(s): 2 collection(s) + 1 uncollected

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ Matrix Generation Summary
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ Total Jobs: 3
✅ Output File: ./artifacts/test-matrices/split-tests-matrix.json

ℹ️ Breakdown by Project:

ℹ️   📦 Aspire.Hosting.Tests (collection mode): 2 collection(s) + 1 uncollected

✅ Matrix generation complete! ✨
```

**Expected JSON Output**:

```json
{
  "include": [
    {
      "type": "collection",
      "name": "DatabaseTests",
      "shortname": "Collection_DatabaseTests",
      "projectName": "Aspire.Hosting.Tests",
      "testProjectPath": "tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj",
      "filterArg": "--filter-collection \"DatabaseTests\"",
      "requiresNugets": false,
      "requiresTestSdk": false,
      "testSessionTimeout": "25m",
      "testHangTimeout": "12m",
      "enablePlaywrightInstall": false
    },
    {
      "type": "collection",
      "name": "ContainerTests",
      "shortname": "Collection_ContainerTests",
      "projectName": "Aspire.Hosting.Tests",
      "testProjectPath": "tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj",
      "filterArg": "--filter-collection \"ContainerTests\"",
      "requiresNugets": false,
      "requiresTestSdk": false,
      "testSessionTimeout": "25m",
      "testHangTimeout": "12m",
      "enablePlaywrightInstall": false
    },
    {
      "type": "uncollected",
      "name": "UncollectedTests",
      "shortname": "Uncollected",
      "projectName": "Aspire.Hosting.Tests",
      "testProjectPath": "tests/Aspire.Hosting.Tests/Aspire.Hosting.Tests.csproj",
      "filterArg": "--filter-not-collection \"DatabaseTests\" --filter-not-collection \"ContainerTests\"",
      "requiresNugets": false,
      "requiresTestSdk": false,
      "testSessionTimeout": "15m",
      "testHangTimeout": "8m",
      "enablePlaywrightInstall": false
    }
  ]
}
```

### Test 2: Class Mode

Create test files:

```bash
# artifacts/helix/Aspire.Templates.Tests.tests.list
class:Aspire.Templates.Tests.BuildAndRunTemplateTests
class:Aspire.Templates.Tests.EmptyTemplateRunTests
class:Aspire.Templates.Tests.StarterTemplateRunTests
```

```json
// artifacts/helix/Aspire.Templates.Tests.tests.metadata.json
{
  "projectName": "Aspire.Templates.Tests",
  "testClassNamesPrefix": "Aspire.Templates.Tests",
  "testProjectPath": "tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj",
  "mode": "class",
  "collections": "",
  "testSessionTimeout": "20m",
  "testHangTimeout": "10m",
  "requiresNugets": "true",
  "requiresTestSdk": "true",
  "enablePlaywrightInstall": "true"
}
```

Run script:

```powershell
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected Console Output**:
```
✅ Starting matrix generation for BuildOs=linux
...

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ℹ️ Processing: Aspire.Templates.Tests
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
ℹ️   Mode: class
✅   Strategy: Class-based splitting

🔍     ➕ Class: BuildAndRunTemplateTests
🔍     ➕ Class: EmptyTemplateRunTests
🔍     ➕ Class: StarterTemplateRunTests

✅   Generated 3 job(s): one per class

...

ℹ️   📄 Aspire.Templates.Tests (class mode): 3 class(es)

✅ Matrix generation complete! ✨
```

**Expected JSON Output**:

```json
{
  "include": [
    {
      "type": "class",
      "fullClassName": "Aspire.Templates.Tests.BuildAndRunTemplateTests",
      "shortname": "BuildAndRunTemplateTests",
      "projectName": "Aspire.Templates.Tests",
      "testProjectPath": "tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj",
      "filterArg": "--filter-class \"Aspire.Templates.Tests.BuildAndRunTemplateTests\"",
      "requiresNugets": true,
      "requiresTestSdk": true,
      "testSessionTimeout": "20m",
      "testHangTimeout": "10m",
      "enablePlaywrightInstall": true
    },
    {
      "type": "class",
      "fullClassName": "Aspire.Templates.Tests.EmptyTemplateRunTests",
      "shortname": "EmptyTemplateRunTests",
      ...
    },
    {
      "type": "class",
      "fullClassName": "Aspire.Templates.Tests.StarterTemplateRunTests",
      "shortname": "StarterTemplateRunTests",
      ...
    }
  ]
}
```

### Test 3: Mixed Projects

Create files for both projects above, then run:

```powershell
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected**: 6 total jobs (3 from Hosting.Tests + 3 from Templates.Tests)

**Console Summary**:
```
ℹ️ Breakdown by Project:

ℹ️   📦 Aspire.Hosting.Tests (collection mode): 2 collection(s) + 1 uncollected
ℹ️   📄 Aspire.Templates.Tests (class mode): 3 class(es)
```

## Validation

### Verify Matrix Structure

```powershell
# Load matrix
$matrix = Get-Content ./artifacts/test-matrices/split-tests-matrix.json | ConvertFrom-Json

# Check entry count
$matrix.include.Count

# Verify all entries have required fields
$matrix.include | ForEach-Object {
    $required = @('type', 'shortname', 'projectName', 'testProjectPath', 'filterArg')
    foreach ($field in $required) {
        if (-not $_.$field) {
            Write-Error "Missing field: $field in entry: $($_.shortname)"
        }
    }
}

# Check filter arguments
$matrix.include | Select-Object shortname, filterArg | Format-Table

# Group by type
$matrix.include | Group-Object -Property type | Select-Object Name, Count
```

### Verify Filter Arguments Work

```powershell
# Test a collection filter
dotnet test YourTests.dll -- --filter-collection "DatabaseTests"

# Test a class filter
dotnet test YourTests.dll -- --filter-class "Aspire.Templates.Tests.Test1"

# Test uncollected filter
dotnet test YourTests.dll -- --filter-not-collection "DatabaseTests" --filter-not-collection "ContainerTests"
```

## Common Issues

### Issue 1: "Mode is unknown"

**Symptom**: Script skips project with "Unable to determine mode"  
**Cause**: .tests.list file has unexpected format  
**Fix**: Check file format - should have `collection:` or `class:` prefixes

### Issue 2: Invalid JSON

**Symptom**: GitHub Actions can't parse matrix  
**Cause**: Special characters in names  
**Fix**: Script escapes quotes automatically, but verify with `jq`

```bash
cat split-tests-matrix.json | jq empty
# Should exit with code 0 if valid
```

### Issue 3: Empty filterArg for uncollected

**Symptom**: Uncollected job has empty filter  
**Cause**: No collections to exclude  
**Fix**: This is OK - empty filter runs all tests

## Next Steps

The matrix is now generated! GitHub Actions workflow already consumes it (no changes needed from v1).

Proceed to [Step 4: Project Configuration (v3)](./STEP_04_PROJECT_CONFIG_V3.md)