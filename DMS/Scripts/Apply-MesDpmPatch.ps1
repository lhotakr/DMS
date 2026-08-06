[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [switch]$SkipMainWindowPatch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRootPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$patchRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Write-Step([string]$Text) {
    Write-Host "`n== $Text ==" -ForegroundColor Cyan
}

function Backup-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $backupPath = "$Path.bak-$timestamp"
    Copy-Item -LiteralPath $Path -Destination $backupPath -Force
    Write-Host "Backup: $backupPath"
}

function Copy-Tree([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Patch source does not exist: $Source"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
    Write-Host "Copied: $Source -> $Destination"
}

function Read-Json([string]$Path) {
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Write-Json([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Merge-ArrayByCode(
    [string]$TargetPath,
    [string]$UpsertPath) {

    $upsert = Read-Json $UpsertPath

    if (Test-Path -LiteralPath $TargetPath) {
        Backup-File $TargetPath
        $items = @(Read-Json $TargetPath)
    }
    else {
        New-Item -ItemType Directory -Path (Split-Path $TargetPath) -Force | Out-Null
        $items = @()
    }

    $result = [System.Collections.Generic.List[object]]::new()
    $wasReplaced = $false

    foreach ($item in $items) {
        if ($null -ne $item.code -and
            [string]::Equals(
                [string]$item.code,
                [string]$upsert.code,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            $result.Add($upsert)
            $wasReplaced = $true
        }
        else {
            $result.Add($item)
        }
    }

    if (-not $wasReplaced) {
        $result.Add($upsert)
    }

    Write-Json $TargetPath ($result.ToArray())
    Write-Host "Upserted $($upsert.code): $TargetPath"
}

function Merge-Localization(
    [string]$TargetPath,
    [string]$UpsertPath) {

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        throw "Localization file does not exist: $TargetPath"
    }

    Backup-File $TargetPath

    $targetObject = Get-Content -LiteralPath $TargetPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $upsertObject = Get-Content -LiteralPath $UpsertPath -Raw -Encoding UTF8 |
        ConvertFrom-Json

    $target = @{}
    foreach ($property in $targetObject.PSObject.Properties) {
        $target[$property.Name] = $property.Value
    }

    foreach ($property in $upsertObject.PSObject.Properties) {
        $target[$property.Name] = $property.Value
    }

    $ordered = [ordered]@{}
    foreach ($key in ($target.Keys | Sort-Object)) {
        $ordered[$key] = $target[$key]
    }

    Write-Json $TargetPath $ordered
    Write-Host "Merged localization: $TargetPath"
}

function Insert-Once(
    [string]$Text,
    [string]$Marker,
    [string]$Insertion,
    [string]$AlreadyPresent) {

    if ($Text.Contains($AlreadyPresent)) {
        return $Text
    }

    $index = $Text.IndexOf($Marker, [System.StringComparison]::Ordinal)
    if ($index -lt 0) {
        throw "MainWindow patch marker was not found: $Marker"
    }

    return $Text.Insert($index, $Insertion)
}

if (-not (Test-Path -LiteralPath $projectRootPath)) {
    throw "Project root does not exist: $projectRootPath"
}

Write-Step 'Copy C# and XAML files'
Copy-Tree `
    (Join-Path $patchRoot 'src\DMS.Core\Mes') `
    (Join-Path $projectRootPath 'src\DMS.Core\Mes')
Copy-Tree `
    (Join-Path $patchRoot 'src\DMS.Core\Transactions\Handlers') `
    (Join-Path $projectRootPath 'src\DMS.Core\Transactions\Handlers')
Copy-Tree `
    (Join-Path $patchRoot 'src\DMS.Desktop\Configuration\Mes') `
    (Join-Path $projectRootPath 'src\DMS.Desktop\Configuration\Mes')
Copy-Tree `
    (Join-Path $patchRoot 'src\DMS.Desktop\Services\Mes') `
    (Join-Path $projectRootPath 'src\DMS.Desktop\Services\Mes')
Copy-Tree `
    (Join-Path $patchRoot 'src\DMS.Desktop\Views\Mes') `
    (Join-Path $projectRootPath 'src\DMS.Desktop\Views\Mes')

Write-Step 'Install MES configuration templates safely'
$configTarget = Join-Path $projectRootPath 'Config'
New-Item -ItemType Directory -Path $configTarget -Force | Out-Null

foreach ($name in @('mes-integration.json', 'mes-plc-bindings.json')) {
    $source = Join-Path $patchRoot "Config\$name"
    $target = Join-Path $configTarget $name

    if (Test-Path -LiteralPath $target) {
        Write-Warning "$name already exists and was not overwritten. Compare it with $source."
    }
    else {
        Copy-Item -LiteralPath $source -Destination $target
        Write-Host "Created: $target"
    }
}

Copy-Item `
    -LiteralPath (Join-Path $patchRoot 'Config\devices.example.txt') `
    -Destination (Join-Path $configTarget 'devices.example.txt') `
    -Force
Copy-Item `
    -LiteralPath (Join-Path $patchRoot 'Config\mes-plc-bindings.mapping-example.json') `
    -Destination (Join-Path $configTarget 'mes-plc-bindings.mapping-example.json') `
    -Force

Write-Step 'Upsert transaction and module metadata'
Merge-ArrayByCode `
    (Join-Path $configTarget 'transactions.json') `
    (Join-Path $patchRoot 'Patches\transactions.mesdpm.upsert.json')
Merge-ArrayByCode `
    (Join-Path $configTarget 'dms-modules.json') `
    (Join-Path $patchRoot 'Patches\dms-modules.mes.upsert.json')

Write-Step 'Merge localization keys'
$localizationTarget = Join-Path $configTarget 'Localization'
foreach ($culture in @('cs-CZ', 'en-US', 'de-DE')) {
    Merge-Localization `
        (Join-Path $localizationTarget "$culture.json") `
        (Join-Path $patchRoot "Config\LocalizationUpsert\$culture.mesdpm.upsert.json")
}

if (-not $SkipMainWindowPatch) {
    Write-Step 'Patch MainWindow shell integration'

    $mainWindowPath = Join-Path $projectRootPath 'src\DMS.Desktop\Views\MainWindow.xaml.cs'
    if (-not (Test-Path -LiteralPath $mainWindowPath)) {
        throw "MainWindow was not found: $mainWindowPath"
    }

    Backup-File $mainWindowPath
    $text = Get-Content -LiteralPath $mainWindowPath -Raw -Encoding UTF8

    if (-not $text.Contains('using DMS.Desktop.Views.Mes;')) {
        $usingMarker = if ($text.Contains('using DMS.Desktop.Views.Settings;')) {
            'using DMS.Desktop.Views.Settings;'
        }
        elseif ($text.Contains('using DMS.Desktop.Views.Help;')) {
            'using DMS.Desktop.Views.Help;'
        }
        else {
            'using System.IO;'
        }

        $usingIndex = $text.IndexOf($usingMarker, [System.StringComparison]::Ordinal)
        if ($usingIndex -lt 0) {
            throw 'Could not find a safe using insertion point in MainWindow.xaml.cs.'
        }

        $lineEnd = $text.IndexOf("`n", $usingIndex)
        $text = $text.Insert($lineEnd + 1, "using DMS.Desktop.Views.Mes;`r`n")
    }

    if (-not $text.Contains('new MesDataPointMonitorTransactionHandler()')) {
        $handlerMarker = '    // fallback / obecné'
        $handlerIndex = $text.IndexOf($handlerMarker, [System.StringComparison]::Ordinal)

        if ($handlerIndex -lt 0) {
            $handlerMarker = '    new SimpleMessageTransactionHandler("SimpleMessage"'
            $handlerIndex = $text.IndexOf($handlerMarker, [System.StringComparison]::Ordinal)
        }

        if ($handlerIndex -lt 0) {
            throw 'Could not find the transaction-handler insertion point.'
        }

        $text = $text.Insert(
            $handlerIndex,
            "    // MES`r`n    new MesDataPointMonitorTransactionHandler(),`r`n`r`n")
    }

    if (-not $text.Contains('case "MESDPM":')) {
        $renderMethodIndex = $text.IndexOf(
            'private void RenderTransactionResult',
            [System.StringComparison]::Ordinal)

        if ($renderMethodIndex -lt 0) {
            throw 'RenderTransactionResult was not found.'
        }

        $defaultIndex = $text.IndexOf(
            '            default:',
            $renderMethodIndex,
            [System.StringComparison]::Ordinal)

        if ($defaultIndex -lt 0) {
            throw 'The default branch in RenderTransactionResult was not found.'
        }

        $caseBlock = @"
            case "MESDPM":
                RenderMesDataPointMonitor(result.Parameter);
                break;

"@
        $text = $text.Insert($defaultIndex, $caseBlock)
    }

    if (-not $text.Contains('private void RenderMesDataPointMonitor(')) {
        $methodMarker = '    private void RenderSimplePage('
        $methodIndex = $text.IndexOf($methodMarker, [System.StringComparison]::Ordinal)

        if ($methodIndex -lt 0) {
            throw 'RenderSimplePage insertion marker was not found.'
        }

        $methodBlock = @"
    private void RenderMesDataPointMonitor(string? query)
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new MesDataPointMonitorView(
                query,
                _appSettings.ConfigurationRootPath,
                _logger,
                _currentUser.DisplayName,
                key => T(key)));

        ResetWorkspaceScroll();
    }

"@
        $text = $text.Insert($methodIndex, $methodBlock)
    }

    [System.IO.File]::WriteAllText(
        $mainWindowPath,
        $text,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Patched: $mainWindowPath"
}

Write-Step 'Finished'
Write-Host '1. Set Config\mes-integration.json -> devicesFilePath to the existing live devices.txt.' -ForegroundColor Yellow
Write-Host '2. Do NOT copy or maintain a second devices.txt.' -ForegroundColor Yellow
Write-Host '3. Fill confirmed zero-based Modbus addresses in Config\mes-plc-bindings.json.' -ForegroundColor Yellow
Write-Host '4. Close Visual Studio, delete bin/obj, then Clean + Rebuild.' -ForegroundColor Yellow
Write-Host '5. Run MESDPM X5-1.' -ForegroundColor Green
