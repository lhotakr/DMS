param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Step([string]$text) {
    Write-Host "[MES SAFE] $text" -ForegroundColor Cyan
}

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$PackageRoot = Split-Path -Parent $PSScriptRoot

if (Test-Path -LiteralPath (Join-Path $ProjectRoot "DMS\src\DMS.Desktop")) {
    $SolutionRoot = Join-Path $ProjectRoot "DMS"
}
elseif (Test-Path -LiteralPath (Join-Path $ProjectRoot "src\DMS.Desktop")) {
    $SolutionRoot = $ProjectRoot
}
else {
    throw "Could not resolve DMS solution root. Expected either '$ProjectRoot\DMS\src' or '$ProjectRoot\src'."
}

$files = @(
    "src\DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml",
    "src\DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml.cs",
    "src\DMS.Integration.Mes\Live\MesLiveOverviewModels.cs",
    "src\DMS.Integration.Mes\Live\MesLiveOverviewDataService.cs"
)

Step "Solution root: $SolutionRoot"
Step "Preflight: exact four source files only."

foreach ($relative in $files) {
    $source = Join-Path $PackageRoot $relative
    $target = Join-Path $SolutionRoot $relative

    if (-not (Test-Path -LiteralPath $source)) {
        throw "Patch source file is missing: $source"
    }

    if (-not (Test-Path -LiteralPath $target)) {
        throw "Target file is missing: $target"
    }
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $ProjectRoot "DMS_PatchBackups\MES_MultiSelect_SAFE_v2_1_$stamp"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

Step "Backup: $backupRoot"

foreach ($relative in $files) {
    $target = Join-Path $SolutionRoot $relative
    $backup = Join-Path $backupRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    Copy-Item -LiteralPath $target -Destination $backup -Force
}

Step "Replacing four MES source files."

foreach ($relative in $files) {
    $source = Join-Path $PackageRoot $relative
    $target = Join-Path $SolutionRoot $relative
    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-Host "  updated: $target" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done. No transactions.json, localization, modules, MainWindow or runtime config were touched." -ForegroundColor Green
Write-Host "Next: Clean Solution -> Rebuild Solution -> run MES." -ForegroundColor Cyan
