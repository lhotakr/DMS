param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$DmsConfigRoot = "",
    [string]$DefaultMesDevicesFilePath = "\\10.131.10.5\FISData\devices.txt",
    [switch]$KeepNet00
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$message) {
    Write-Host "[MES] $message" -ForegroundColor Cyan
}

function Resolve-DesktopRoot([string]$root) {
    $candidate1 = Join-Path $root "src\DMS.Desktop"
    if (Test-Path $candidate1) { return (Resolve-Path $candidate1).Path }

    if ((Split-Path $root -Leaf) -eq "DMS.Desktop") {
        return (Resolve-Path $root).Path
    }

    throw "Cannot find src\DMS.Desktop under ProjectRoot: $root"
}

function Get-AppSettingValue([object]$json, [string[]]$names) {
    foreach ($name in $names) {
        $prop = $json.PSObject.Properties | Where-Object { $_.Name -ieq $name } | Select-Object -First 1
        if ($null -ne $prop -and -not [string]::IsNullOrWhiteSpace([string]$prop.Value)) {
            return [string]$prop.Value
        }
    }
    return ""
}

function Save-Json($path, $value) {
    $json = $value | ConvertTo-Json -Depth 40
    [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
}


function Set-JsonPropertyIfMissingOrEmpty([string]$path, [string]$propertyName, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($path) -or [string]::IsNullOrWhiteSpace($propertyName)) { return }
    if (-not (Test-Path $path)) { return }

    $raw = Get-Content -Raw -Encoding UTF8 $path
    if ([string]::IsNullOrWhiteSpace($raw)) { return }

    $json = $raw | ConvertFrom-Json
    if ($null -eq $json) { return }

    $prop = $json.PSObject.Properties | Where-Object { $_.Name -ieq $propertyName } | Select-Object -First 1
    if ($null -eq $prop) {
        $json | Add-Member -NotePropertyName $propertyName -NotePropertyValue $value
        Save-Json $path $json
        Write-Step "Updated $(Split-Path $path -Leaf): added $propertyName=$value"
        return
    }

    if ([string]::IsNullOrWhiteSpace([string]$prop.Value)) {
        $prop.Value = $value
        Save-Json $path $json
        Write-Step "Updated $(Split-Path $path -Leaf): set $propertyName=$value"
    }
}

function Upsert-JsonArrayByCode([string]$targetPath, [string]$objectPath) {
    $item = Get-Content -Raw -Encoding UTF8 $objectPath | ConvertFrom-Json

    if (Test-Path $targetPath) {
        $existingRaw = Get-Content -Raw -Encoding UTF8 $targetPath
        if ([string]::IsNullOrWhiteSpace($existingRaw)) {
            $rows = @()
        } else {
            $loaded = $existingRaw | ConvertFrom-Json
            $rows = @($loaded)
        }
    } else {
        $rows = @()
    }

    $rows = @($rows | Where-Object { $_.code -ne $item.code })
    $rows += $item
    $rows = @($rows | Sort-Object @{ Expression = { if ($_.sortOrder) { [int]$_.sortOrder } else { 9999 } } }, code)
    Save-Json $targetPath $rows
}

function Remove-JsonArrayCode([string]$targetPath, [string]$code) {
    if (-not (Test-Path $targetPath)) { return }
    $existingRaw = Get-Content -Raw -Encoding UTF8 $targetPath
    if ([string]::IsNullOrWhiteSpace($existingRaw)) { return }
    $rows = @($existingRaw | ConvertFrom-Json)
    $before = $rows.Count
    $rows = @($rows | Where-Object { $_.code -ne $code })
    if ($rows.Count -ne $before) {
        Save-Json $targetPath $rows
        Write-Step "Removed obsolete transaction/config code $code"
    }
}

function Read-JsonObjectAsHashtable([string]$path) {
    $result = [ordered]@{}
    if (-not (Test-Path $path)) { return $result }

    $raw = Get-Content -Raw -Encoding UTF8 $path
    if ([string]::IsNullOrWhiteSpace($raw)) { return $result }

    $obj = $raw | ConvertFrom-Json
    if ($null -eq $obj) { return $result }

    foreach ($prop in $obj.PSObject.Properties) {
        $result[$prop.Name] = $prop.Value
    }

    return $result
}

function Upsert-Localization([string]$targetPath, [string]$upsertPath) {
    $target = Read-JsonObjectAsHashtable $targetPath
    $upsert = Read-JsonObjectAsHashtable $upsertPath

    foreach ($key in $upsert.Keys) {
        $target[$key] = $upsert[$key]
    }

    $ordered = [ordered]@{}
    foreach ($key in ($target.Keys | Sort-Object)) {
        $ordered[$key] = $target[$key]
    }

    $dir = Split-Path $targetPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    Save-Json $targetPath $ordered
}

function Add-LineOnceBefore([string]$text, [string]$markerPattern, [string]$line, [string]$presencePattern) {
    if ($text -match $presencePattern) { return $text }
    $match = [regex]::Match($text, $markerPattern)
    if (-not $match.Success) { return $text }
    return $text.Insert($match.Index, $line + "`r`n")
}

$patchRoot = Split-Path $PSScriptRoot -Parent
$desktopRoot = Resolve-DesktopRoot $ProjectRoot
Write-Step "Desktop project: $desktopRoot"

# Copy C# / XAML files.
# Note:
# When the patch ZIP is extracted directly into the project root, $sourceDesktop and
# $desktopRoot can point to the same physical folder. In that case Copy-Item would
# try to overwrite files with themselves and PowerShell stops with:
# "Cannot overwrite the item ... with itself." We skip same-path files and continue
# with config/transaction/localization merging.
$sourceDesktop = Join-Path $patchRoot "src\DMS.Desktop"
if (Test-Path $sourceDesktop) {
    $sourceDesktopResolved = (Resolve-Path $sourceDesktop).Path.TrimEnd('\','/')
    $desktopRootResolved = (Resolve-Path $desktopRoot).Path.TrimEnd('\','/')

    Get-ChildItem -Path $sourceDesktop -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($sourceDesktopResolved.Length).TrimStart('\','/')
        $target = Join-Path $desktopRootResolved $relative
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir | Out-Null }

        $sourceFull = $_.FullName
        $targetFull = $target
        if (Test-Path $targetFull) {
            $targetFull = (Resolve-Path $targetFull).Path
        }

        if ([string]::Equals($sourceFull, $targetFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Step "Skipped self-copy $relative"
            return
        }

        Copy-Item $sourceFull $target -Force
        Write-Step "Copied $relative"
    }
} else {
    Write-Warning "Patch source folder not found: $sourceDesktop. Skipping file copy and continuing with config merge."
}

# Try to resolve DMS shared Config root.
if ([string]::IsNullOrWhiteSpace($DmsConfigRoot)) {
    $appSettingsPath = Join-Path $desktopRoot "Config\appsettings.json"
    if (Test-Path $appSettingsPath) {
        try {
            $appSettings = Get-Content -Raw -Encoding UTF8 $appSettingsPath | ConvertFrom-Json
            $DmsConfigRoot = Get-AppSettingValue $appSettings @("ConfigurationRootPath", "configurationRootPath")
        } catch {
            $DmsConfigRoot = ""
        }
    }
}

if ([string]::IsNullOrWhiteSpace($DmsConfigRoot)) {
    $DmsConfigRoot = Join-Path $desktopRoot "Config"
    Write-Step "DmsConfigRoot not resolved from appsettings; using project Config fallback: $DmsConfigRoot"
}

if (-not (Test-Path $DmsConfigRoot)) {
    New-Item -ItemType Directory -Path $DmsConfigRoot | Out-Null
}
Write-Step "DMS Config root: $DmsConfigRoot"

# Merge transaction/module config.
$transactionsPath = Join-Path $DmsConfigRoot "transactions.json"
$modulesPath = Join-Path $DmsConfigRoot "dms-modules.json"

if (-not $KeepNet00) {
    Remove-JsonArrayCode $transactionsPath "NET00"
}

Upsert-JsonArrayByCode $transactionsPath (Join-Path $patchRoot "Config\mes00-transaction.json")
Upsert-JsonArrayByCode $transactionsPath (Join-Path $patchRoot "Config\mes02-transaction.json")
Upsert-JsonArrayByCode $transactionsPath (Join-Path $patchRoot "Config\mes03-transaction.json")
Write-Step "Merged transactions.json -> MES00, MES02, MES03"

Upsert-JsonArrayByCode $modulesPath (Join-Path $patchRoot "Config\mes-module.json")
Write-Step "Merged dms-modules.json -> MES"

# Copy device list only if not already present.
$devicesTarget = Join-Path $DmsConfigRoot "devices.txt"
if (-not (Test-Path $devicesTarget)) {
    Copy-Item (Join-Path $patchRoot "Config\devices.txt") $devicesTarget
    Write-Step "Copied sample devices.txt"
} else {
    Write-Step "devices.txt already exists - left unchanged"
}

# Copy settings only if not already present, then make sure the devices.txt path is configurable.
$settingsTarget = Join-Path $DmsConfigRoot "mes-communication-settings.json"
if (-not (Test-Path $settingsTarget)) {
    Copy-Item (Join-Path $patchRoot "Config\mes-communication-settings.json") $settingsTarget
    Write-Step "Copied default mes-communication-settings.json"
} else {
    Write-Step "mes-communication-settings.json already exists - left unchanged"
}

if (-not [string]::IsNullOrWhiteSpace($DefaultMesDevicesFilePath)) {
    Set-JsonPropertyIfMissingOrEmpty $settingsTarget "devicesFilePath" $DefaultMesDevicesFilePath
}

# Localization upserts.
$locRoot = Join-Path $DmsConfigRoot "Localization"
Upsert-Localization (Join-Path $locRoot "cs-CZ.json") (Join-Path $patchRoot "Config\Localization\Upserts\cs-CZ.MES.json")
Upsert-Localization (Join-Path $locRoot "en-US.json") (Join-Path $patchRoot "Config\Localization\Upserts\en-US.MES.json")
Upsert-Localization (Join-Path $locRoot "de-DE.json") (Join-Path $patchRoot "Config\Localization\Upserts\de-DE.MES.json")
Write-Step "Merged localization keys"

# Patch MainWindow dispatcher glue.
$mainCandidates = @(
    (Join-Path $desktopRoot "Views\MainWindow.xaml.cs"),
    (Join-Path $desktopRoot "MainWindow.xaml.cs")
)
$mainPath = $mainCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($null -ne $mainPath) {
    $text = Get-Content -Raw -Encoding UTF8 $mainPath

    $handlerLines = @(
        '    new SimpleMessageTransactionHandler("MesCommunicationSettings", "MES nastavení komunikace"),',
        '    new SimpleMessageTransactionHandler("MesDeviceEditor", "MES editace zařízení"),',
        '    new SimpleMessageTransactionHandler("MesDeviceMonitor", "MES monitor zařízení"),'
    )

    foreach ($line in $handlerLines) {
        $key = ($line -replace '^\s*new SimpleMessageTransactionHandler\("([^"]+)".*$', '$1')
        if ($text -notmatch [regex]::Escape($key)) {
            if ($text -match '    new SimpleMessageTransactionHandler\("SimpleMessage",') {
                $text = [regex]::Replace($text, '    new SimpleMessageTransactionHandler\("SimpleMessage",', $line + "`r`n" + '    new SimpleMessageTransactionHandler("SimpleMessage",', 1)
            } elseif ($text -match '    new SimpleMessageTransactionHandler\("LogViewer",\s*"[^"]+"\),') {
                $text = [regex]::Replace($text, '    new SimpleMessageTransactionHandler\("LogViewer",\s*"[^"]+"\),', { param($m) $m.Value + "`r`n" + $line }, 1)
            } else {
                Write-Warning "Could not find handler insertion point. Add manually: $line"
            }
        }
    }

    if ($text -notmatch 'case\s+"MES00"') {
        $caseBlock = @'
            case "MES00":
                RenderMesCommunicationSettings();
                break;

            case "MES02":
                RenderMesDeviceEditor();
                break;

            case "MES03":
                RenderMesDeviceMonitor();
                break;

'@
        if ($text -match '            default:') {
            $text = [regex]::Replace($text, '            default:', $caseBlock + '            default:', 1)
            Write-Step "Patched RenderTransactionResult switch -> MES00/MES02/MES03"
        } else {
            Write-Warning "Could not find switch insertion point. Add manually cases MES00/MES02/MES03."
        }
    } else {
        Write-Step "RenderTransactionResult already contains MES00/MES02/MES03"
    }

    [System.IO.File]::WriteAllText($mainPath, $text, [System.Text.UTF8Encoding]::new($false))
    Write-Step "Patched MainWindow: $mainPath"
} else {
    Write-Warning "MainWindow.xaml.cs not found. Add MES handlers/switch manually."
}

Write-Step "Done. Rebuild DMS.Desktop and run MES00, MES02, MES03."
