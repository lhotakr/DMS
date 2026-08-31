param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$packageRoot = Split-Path -Parent $PSScriptRoot

$srcRootCandidates = @(
    (Join-Path $ProjectRoot "DMS\src"),
    (Join-Path $ProjectRoot "src")
)

$srcRoot = $srcRootCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $srcRoot) {
    throw "Could not find DMS source root under '$ProjectRoot'. Expected DMS\src or src."
}

$files = @(
    @{ Source = "src\DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml"; Target = "DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml" },
    @{ Source = "src\DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml.cs"; Target = "DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml.cs" },
    @{ Source = "src\DMS.Desktop\Views\MainWindow.MesLiveOverview.cs"; Target = "DMS.Desktop\Views\MainWindow.MesLiveOverview.cs" },
    @{ Source = "src\DMS.Integration.Mes\Live\MesLiveOverviewDataService.cs"; Target = "DMS.Integration.Mes\Live\MesLiveOverviewDataService.cs" },
    @{ Source = "src\DMS.Integration.Mes\Live\MesLiveOverviewModels.cs"; Target = "DMS.Integration.Mes\Live\MesLiveOverviewModels.cs" }
)

# Safety preflight: this installer is allowed to CREATE files only.
# If any target already exists, abort before copying anything.
foreach ($item in $files) {
    $target = Join-Path $srcRoot $item.Target
    if (Test-Path -LiteralPath $target) {
        throw "Safety stop: target already exists, nothing was changed: $target"
    }
}

foreach ($item in $files) {
    $source = Join-Path $packageRoot $item.Source
    $target = Join-Path $srcRoot $item.Target

    if (-not (Test-Path -LiteralPath $source)) {
        throw "Package file missing: $source"
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    Write-Host "created: $target" -ForegroundColor Green
}

Write-Host "" 
Write-Host "New source files copied. Existing DMS files/config/localization were NOT modified." -ForegroundColor Cyan
Write-Host "Next: add the single MES case from Patch\Snippets\MainWindow_RenderSwitch.txt, rebuild, then create MES in SYS11 by duplicating MES06." -ForegroundColor Cyan
