param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$DmsConfigRoot = "",
    [string]$DefaultMesDevicesFilePath = "\\10.131.10.5\FISData\devices.txt"
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$message) { Write-Host "[MES-V4] $message" -ForegroundColor Cyan }

function Resolve-ProjectRoot([string]$root) {
    if (Test-Path (Join-Path $root "src\DMS.Desktop")) { return (Resolve-Path $root).Path }
    if ((Split-Path $root -Leaf) -eq "DMS.Desktop") { return (Resolve-Path (Join-Path $root "..\..")).Path }
    throw "Cannot find src\DMS.Desktop under ProjectRoot: $root"
}

function Save-Json($path, $value) {
    $json = $value | ConvertTo-Json -Depth 60
    [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
}

function Upsert-JsonArrayByCode([string]$targetPath, [string]$objectPath) {
    $item = Get-Content -Raw -Encoding UTF8 $objectPath | ConvertFrom-Json
    if (Test-Path $targetPath) {
        $raw = Get-Content -Raw -Encoding UTF8 $targetPath
        $rows = if ([string]::IsNullOrWhiteSpace($raw)) { @() } else { @($raw | ConvertFrom-Json) }
    } else { $rows = @() }
    $rows = @($rows | Where-Object { $_.code -ne $item.code })
    $rows += $item
    $rows = @($rows | Sort-Object @{ Expression = { if ($_.sortOrder) { [int]$_.sortOrder } else { 9999 } } }, code)
    Save-Json $targetPath $rows
}

function Read-JsonObjectAsHashtable([string]$path) {
    $result = [ordered]@{}
    if (-not (Test-Path $path)) { return $result }
    $raw = Get-Content -Raw -Encoding UTF8 $path
    if ([string]::IsNullOrWhiteSpace($raw)) { return $result }
    $obj = $raw | ConvertFrom-Json
    if ($null -eq $obj) { return $result }
    foreach ($prop in $obj.PSObject.Properties) { $result[$prop.Name] = $prop.Value }
    return $result
}

function Upsert-Localization([string]$targetPath, [string]$upsertPath) {
    $target = Read-JsonObjectAsHashtable $targetPath
    $upsert = Read-JsonObjectAsHashtable $upsertPath
    foreach ($key in $upsert.Keys) { $target[$key] = $upsert[$key] }
    $ordered = [ordered]@{}
    foreach ($key in ($target.Keys | Sort-Object)) { $ordered[$key] = $target[$key] }
    $dir = Split-Path $targetPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    Save-Json $targetPath $ordered
}

function Copy-PatchFolder([string]$source, [string]$target) {
    if (-not (Test-Path $source)) { return }
    $sourceResolved = (Resolve-Path $source).Path.TrimEnd('\','/')
    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target | Out-Null }
    $targetResolved = (Resolve-Path $target).Path.TrimEnd('\','/')
    Get-ChildItem -Path $source -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($sourceResolved.Length).TrimStart('\','/')
        $dest = Join-Path $targetResolved $relative
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir | Out-Null }
        $srcFull = $_.FullName
        $dstFull = $dest
        if (Test-Path $dstFull) { $dstFull = (Resolve-Path $dstFull).Path }
        if ([string]::Equals($srcFull, $dstFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Step "Skipped self-copy $relative"
            return
        }
        Copy-Item $srcFull $dest -Force
        Write-Step "Copied $relative"
    }
}

function Ensure-ProjectReference([string]$csprojPath, [string]$includePath) {
    if (-not (Test-Path $csprojPath)) { return }
    $text = Get-Content -Raw -Encoding UTF8 $csprojPath
    if ($text -match [regex]::Escape($includePath)) { return }
    $line = "    <ProjectReference Include=`"$includePath`" />"
    if ($text -match "</ItemGroup>") {
        $text = [regex]::Replace($text, "</ItemGroup>", $line + "`r`n  </ItemGroup>", 1)
    } else {
        $text = $text -replace "</Project>", "  <ItemGroup>`r`n$line`r`n  </ItemGroup>`r`n`r`n</Project>"
    }
    [System.IO.File]::WriteAllText($csprojPath, $text, [System.Text.UTF8Encoding]::new($true))
    Write-Step "Added project reference $includePath"
}

function Patch-MainWindow([string]$mainPath) {
    if (-not (Test-Path $mainPath)) { Write-Warning "MainWindow not found: $mainPath"; return }
    $text = Get-Content -Raw -Encoding UTF8 $mainPath

    $handlerBlock = "new SimpleMessageTransactionHandler(`"MesStationData`", `"MES data stanic`"),`r`n    new SimpleMessageTransactionHandler(`"MesWorkplaceOverview`", `"MES soupis pracovišť`"),"

    if ($text -match 'new SimpleMessageTransactionHandler\("MesDeviceMonitor",\s*"[^"]+"\),') {
        $text = [regex]::Replace(
            $text,
            'new SimpleMessageTransactionHandler\("MesDeviceMonitor",\s*"[^"]+"\),',
            $handlerBlock,
            1)
    } elseif ($text -notmatch 'MesStationData') {
        $text = [regex]::Replace(
            $text,
            '(new SimpleMessageTransactionHandler\("MesDeviceEditor",\s*"[^"]+"\),)',
            "`$1`r`n    $handlerBlock",
            1)
    }

    $caseBlock = "case `"MES03`":`r`n                RenderMesStationData();`r`n                break;`r`n`r`n            case `"MES05`":`r`n                RenderMesWorkplaceOverview();`r`n                break;"

    if ($text -match 'case\s+"MES03":\s*\r?\n\s*RenderMesDeviceMonitor\(\);\s*\r?\n\s*break;') {
        $text = [regex]::Replace(
            $text,
            'case\s+"MES03":\s*\r?\n\s*RenderMesDeviceMonitor\(\);\s*\r?\n\s*break;',
            $caseBlock,
            1)
    } elseif ($text -notmatch 'case\s+"MES05"') {
        $text = [regex]::Replace(
            $text,
            '(case\s+"MES03":\s*\r?\n\s*RenderMesStationData\(\);\s*\r?\n\s*break;)',
            "`$1`r`n`r`n            case `"MES05`":`r`n                RenderMesWorkplaceOverview();`r`n                break;",
            1)
    }

    [System.IO.File]::WriteAllText($mainPath, $text, [System.Text.UTF8Encoding]::new($true))
    Write-Step "Patched MainWindow MES cases and handlers"
}

$projectRoot = Resolve-ProjectRoot $ProjectRoot
$patchRoot = Split-Path $PSScriptRoot -Parent
$desktopRoot = Join-Path $projectRoot "src\DMS.Desktop"
$integrationRoot = Join-Path $projectRoot "src\DMS.Integration.Mes"
Write-Step "Project root: $projectRoot"

Copy-PatchFolder (Join-Path $patchRoot "src\DMS.Integration.Mes") $integrationRoot
Copy-PatchFolder (Join-Path $patchRoot "src\DMS.Desktop") $desktopRoot

# Remove old Desktop MES backend files after moving them into DMS.Integration.Mes.
$obsolete = @(
    "Models\MesCommunicationSettings.cs",
    "Models\MesDevice.cs",
    "Models\MesProbeResult.cs",
    "Models\MesMonitorSnapshot.cs",
    "Services\MesCommunicationSettingsService.cs",
    "Services\MesDeviceFileService.cs",
    "Services\MesProbeService.cs",
    "Views\Mes\MesDeviceMonitorView.xaml",
    "Views\Mes\MesDeviceMonitorView.xaml.cs"
)
foreach ($relative in $obsolete) {
    $path = Join-Path $desktopRoot $relative
    if (Test-Path $path) { Remove-Item $path -Force; Write-Step "Removed obsolete $relative" }
}
$class1 = Join-Path $integrationRoot "Class1.cs"
if (Test-Path $class1) { Remove-Item $class1 -Force; Write-Step "Removed obsolete DMS.Integration.Mes\Class1.cs" }

Ensure-ProjectReference (Join-Path $desktopRoot "DMS.Desktop.csproj") "..\DMS.Integration.Mes\DMS.Integration.Mes.csproj"

Patch-MainWindow (Join-Path $desktopRoot "Views\MainWindow.xaml.cs")

# Resolve DMS config root.
if ([string]::IsNullOrWhiteSpace($DmsConfigRoot)) {
    $appSettingsPath = Join-Path $desktopRoot "Config\appsettings.json"
    if (Test-Path $appSettingsPath) {
        try {
            $app = Get-Content -Raw -Encoding UTF8 $appSettingsPath | ConvertFrom-Json
            $prop = $app.PSObject.Properties | Where-Object { $_.Name -ieq "ConfigurationRootPath" -or $_.Name -ieq "configurationRootPath" } | Select-Object -First 1
            if ($null -ne $prop) { $DmsConfigRoot = [string]$prop.Value }
        } catch { $DmsConfigRoot = "" }
    }
}
if ([string]::IsNullOrWhiteSpace($DmsConfigRoot)) { $DmsConfigRoot = Join-Path $projectRoot "Config" }
if (-not (Test-Path $DmsConfigRoot)) { New-Item -ItemType Directory -Path $DmsConfigRoot | Out-Null }
Write-Step "DMS config root: $DmsConfigRoot"

$transactionsPath = Join-Path $DmsConfigRoot "transactions.json"
$modulesPath = Join-Path $DmsConfigRoot "dms-modules.json"
Upsert-JsonArrayByCode $transactionsPath (Join-Path $patchRoot "Config\mes03-transaction.json")
Upsert-JsonArrayByCode $transactionsPath (Join-Path $patchRoot "Config\mes05-transaction.json")
Upsert-JsonArrayByCode $modulesPath (Join-Path $patchRoot "Config\mes-module.json")
Write-Step "Merged transactions MES03/MES05 and module MES"

$settingsTarget = Join-Path $DmsConfigRoot "mes-communication-settings.json"
if (-not (Test-Path $settingsTarget)) {
    Copy-Item (Join-Path $patchRoot "Config\mes-communication-settings.json") $settingsTarget -Force
    Write-Step "Copied default mes-communication-settings.json"
} else {
    $settings = Get-Content -Raw -Encoding UTF8 $settingsTarget | ConvertFrom-Json
    if ($null -eq ($settings.PSObject.Properties | Where-Object { $_.Name -ieq "devicesFilePath" } | Select-Object -First 1)) { $settings | Add-Member -NotePropertyName "devicesFilePath" -NotePropertyValue $DefaultMesDevicesFilePath }
    if ($null -eq ($settings.PSObject.Properties | Where-Object { $_.Name -ieq "enableStationDataPolling" } | Select-Object -First 1)) { $settings | Add-Member -NotePropertyName "enableStationDataPolling" -NotePropertyValue $true }
    if ($null -eq ($settings.PSObject.Properties | Where-Object { $_.Name -ieq "stationPollTimeoutMs" } | Select-Object -First 1)) { $settings | Add-Member -NotePropertyName "stationPollTimeoutMs" -NotePropertyValue 1500 }
    if ($null -eq ($settings.PSObject.Properties | Where-Object { $_.Name -ieq "stationAutoRefreshSeconds" } | Select-Object -First 1)) { $settings | Add-Member -NotePropertyName "stationAutoRefreshSeconds" -NotePropertyValue 10 }
    if ($null -eq ($settings.PSObject.Properties | Where-Object { $_.Name -ieq "stationsFilePath" } | Select-Object -First 1)) { $settings | Add-Member -NotePropertyName "stationsFilePath" -NotePropertyValue "mes-stations.json" }
    if ($null -eq ($settings.PSObject.Properties | Where-Object { $_.Name -ieq "stationSnapshotsFolder" } | Select-Object -First 1)) { $settings | Add-Member -NotePropertyName "stationSnapshotsFolder" -NotePropertyValue "" }
    Save-Json $settingsTarget $settings
    Write-Step "Updated mes-communication-settings.json with station data settings"
}

$stationsTarget = Join-Path $DmsConfigRoot "mes-stations.json"
if (-not (Test-Path $stationsTarget)) {
    Copy-Item (Join-Path $patchRoot "Config\mes-stations.json") $stationsTarget -Force
    Write-Step "Copied default mes-stations.json"
} else { Write-Step "mes-stations.json already exists - left unchanged" }

$locRoot = Join-Path $DmsConfigRoot "Localization"
Upsert-Localization (Join-Path $locRoot "cs-CZ.json") (Join-Path $patchRoot "Config\Localization\Upserts\cs-CZ.MES.json")
Upsert-Localization (Join-Path $locRoot "en-US.json") (Join-Path $patchRoot "Config\Localization\Upserts\en-US.MES.json")
Upsert-Localization (Join-Path $locRoot "de-DE.json") (Join-Path $patchRoot "Config\Localization\Upserts\de-DE.MES.json")
Write-Step "Merged MES localization keys"

Write-Step "Done. Rebuild: dotnet clean .\src\DMS.Desktop\DMS.Desktop.csproj ; dotnet build .\src\DMS.Desktop\DMS.Desktop.csproj"
