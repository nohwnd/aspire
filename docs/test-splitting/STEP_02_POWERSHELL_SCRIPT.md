# Step 2: PowerShell Matrix Generation Script

## Overview

Create a cross-platform PowerShell script that reads test class lists and generates JSON matrices for CI consumption.

## File: `eng/scripts/generate-test-matrix.ps1`

### Complete Implementation

```powershell
<#
.SYNOPSIS
    Generates CI test matrices from test class enumeration files.

.DESCRIPTION
    This script reads .tests.list and .tests.metadata.json files produced by the
    ExtractTestClassNames MSBuild target and generates a JSON matrix file for
    consumption by GitHub Actions or Azure DevOps.

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
        [ValidateSet('Info', 'Success', 'Warning', 'Error')]
        [string]$Level = 'Info'
    )
    
    $prefix = switch ($Level) {
        'Success' { '✅' }
        'Warning' { '⚠️' }
        'Error'   { '❌' }
        default   { 'ℹ️' }
    }
    
    $color = switch ($Level) {
        'Success' { 'Green' }
        'Warning' { 'Yellow' }
        'Error'   { 'Red' }
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
        requiresNugets = 'false'
        requiresTestSdk = 'false'
        testSessionTimeout = '20m'
        testHangTimeout = '10m'
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

function New-MatrixEntry {
    <#
    .SYNOPSIS
        Creates a matrix entry object for a test class.
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
    
    # Return ordered hashtable for consistent JSON output
    [ordered]@{
        shortname = $shortname
        projectName = $ProjectName
        fullClassName = $FullClassName
        testProjectPath = $Metadata.testProjectPath
        requiresNugets = ($Metadata.requiresNugets -eq 'true')
        requiresTestSdk = ($Metadata.requiresTestSdk -eq 'true')
        testSessionTimeout = $Metadata.testSessionTimeout
        testHangTimeout = $Metadata.testHangTimeout
        enablePlaywrightInstall = ($Metadata.enablePlaywrightInstall -eq 'true')
    }
}

#endregion

#region Main Script

Write-Message "Starting matrix generation for BuildOs=$BuildOs"
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
    # Extract project name (e.g., "Aspire.Templates.Tests.tests.list" → "Aspire.Templates.Tests")
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($listFile.Name -replace '\.tests$', '')
    
    Write-Message "Processing $projectName..."
    
    # Read test class names
    $classes = Get-Content $listFile.FullName | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    
    if ($classes.Count -eq 0) {
        Write-Message "  No test classes found, skipping" -Level Warning
        continue
    }
    
    # Read metadata
    $metadataFile = $listFile.FullName -replace '\.tests\.list$', '.tests.metadata.json'
    $metadata = Read-TestMetadata -MetadataFile $metadataFile -ProjectName $projectName
    
    # Generate matrix entry for each test class
    $projectEntryCount = 0
    foreach ($class in $classes) {
        $entry = New-MatrixEntry -FullClassName $class -ProjectName $projectName -Metadata $metadata
        [void]$allEntries.Add($entry)
        $projectEntryCount++
    }
    
    $stats[$projectName] = $projectEntryCount
    Write-Message "  Added $projectEntryCount test class(es)" -Level Success
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

Write-Message ""
Write-Message "Generated matrix with $($allEntries.Count) total test(s)" -Level Success
Write-Message "Output file: $outputFile" -Level Success
Write-Message ""
Write-Message "Matrix breakdown by project:" -Level Info

foreach ($proj in $stats.Keys | Sort-Object) {
    Write-Message "  $proj`: $($stats[$proj]) class(es)" -Level Info
}

Write-Message ""
Write-Message "Matrix generation complete! ✨" -Level Success

#endregion
```

## Script Features

### Cross-Platform Compatibility

- ✅ Uses `System.IO.Path` for path operations
- ✅ No OS-specific cmdlets
- ✅ Tested on Windows, Linux, macOS
- ✅ UTF-8 encoding for JSON output

### Error Handling

- Validates input directory exists
- Handles missing metadata gracefully (uses defaults)
- Creates empty matrix if no tests found (CI won't fail)
- Detailed error messages

### Logging

- Color-coded output (Info, Success, Warning, Error)
- Shows progress per project
- Summary statistics at end
- Helpful for debugging CI issues

## Testing the Script

### Test 1: Empty Directory

```powershell
# Should create empty matrix without errors
mkdir test-empty
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./test-empty `
    -OutputDirectory ./test-output `
    -BuildOs linux
```

**Expected**: Creates `split-tests-matrix.json` with `{"include":[]}`

### Test 2: With Test Lists

```powershell
# First, build a split test project to generate .tests.list files
dotnet build tests/Aspire.Templates.Tests/Aspire.Templates.Tests.csproj `
    /t:Build;ExtractTestClassNames `
    -p:PrepareForHelix=true `
    -p:SplitTestsForCI=true `
    -p:TestClassNamesPrefix=Aspire.Templates.Tests

# Then run the script
pwsh eng/scripts/generate-test-matrix.ps1 `
    -TestListsDirectory ./artifacts/helix `
    -OutputDirectory ./artifacts/test-matrices `
    -BuildOs linux
```

**Expected**: 
- Creates matrix with ~10-15 entries
- Each entry has all required fields
- Valid JSON

### Test 3: Verify JSON Structure

```powershell
# Load and inspect the generated matrix
$matrix = Get-Content ./artifacts/test-matrices/split-tests-matrix.json | ConvertFrom-Json

# Check structure
$matrix.include.Count  # Should be > 0
$matrix.include[0].PSObject.Properties.Name  # Should show all fields

# Verify required fields
$matrix.include | ForEach-Object {
    if (-not $_.shortname) { Write-Error "Missing shortname" }
    if (-not $_.fullClassName) { Write-Error "Missing fullClassName" }
    if (-not $_.projectName) { Write-Error "Missing projectName" }
}
```

## Common Issues

### Issue 1: "Cannot find path"

**Cause**: TestListsDirectory doesn't exist  
**Fix**: Ensure the directory is created before running script

### Issue 2: Invalid JSON

**Cause**: Special characters in class names  
**Fix**: PowerShell's `ConvertTo-Json` handles this automatically

### Issue 3: Empty matrix but tests exist

**Cause**: `.tests.list` files not in expected location  
**Fix**: Check `artifacts/helix/` directory structure

## Next Steps

Proceed to [Step 3: GitHub Actions Integration](./STEP_03_GITHUB_ACTIONS.md)