param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = ".",

    [Parameter(Mandatory = $false)]
    [string]$RuntimeConfigRoot = "Z:\SAP\DMS-db\DEV\Config"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Write-Step([string]$Message) {
    Write-Host "[DMS ROLLBACK] $Message" -ForegroundColor Cyan
}

function Copy-WithSafetyBackup(
    [string]$Source,
    [string]$Target,
    [string]$SafetyRoot,
    [string]$ProjectRoot
) {
    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }

    if (Test-Path -LiteralPath $Target) {
        $fullProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
        $fullTarget = [IO.Path]::GetFullPath($Target)
        $projectPrefix = $fullProjectRoot + [IO.Path]::DirectorySeparatorChar

        if ($fullTarget.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            $relative = $fullTarget.Substring($fullProjectRoot.Length).TrimStart('\','/')
        }
        else {
            $relative = Join-Path "_external" (($fullTarget -replace '[:\\/]+', '_'))
        }

        $safetyTarget = Join-Path $SafetyRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $safetyTarget) -Force | Out-Null
        Copy-Item -LiteralPath $Target -Destination $safetyTarget -Force
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Target) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Target -Force
    Write-Host "  restored: $Target" -ForegroundColor Green
}

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $ProjectRoot)) {
    throw "ProjectRoot does not exist: $ProjectRoot"
}

if (-not [string]::IsNullOrWhiteSpace($RuntimeConfigRoot)) {
    $RuntimeConfigRoot = [IO.Path]::GetFullPath($RuntimeConfigRoot)
}

$backupBase = Join-Path (Split-Path -Parent $ProjectRoot) "DMS_PatchBackups"

if (-not (Test-Path -LiteralPath $backupBase)) {
    throw "DMS_PatchBackups was not found: $backupBase"
}

# v1.6 is the important backup: it was created before the first MES Live Overview
# run that actually started modifying the project/configuration.
$v16Backups = @(
    Get-ChildItem -LiteralPath $backupBase -Directory -Filter "MES_LiveOverview_v1_6_*" |
        Sort-Object LastWriteTime -Descending
)

if ($v16Backups.Count -eq 0) {
    $available = @(
        Get-ChildItem -LiteralPath $backupBase -Directory -Filter "MES_LiveOverview_*" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -ExpandProperty FullName
    )

    throw ("No MES_LiveOverview_v1_6_* backup was found.`r`nAvailable backups:`r`n" +
           ($available -join "`r`n"))
}

$backupRoot = $v16Backups[0].FullName
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safetyRoot = Join-Path $backupBase "MES_LiveOverview_BROKEN_before_rollback_$timestamp"
New-Item -ItemType Directory -Path $safetyRoot -Force | Out-Null

Write-Step "Project root: $ProjectRoot"
Write-Step "Restoring clean pre-patch backup: $backupRoot"
Write-Step "Current broken state will be backed up to: $safetyRoot"

# 1) Restore every file that v1.6 backed up from inside ProjectRoot.
$projectBackupFiles = @(
    Get-ChildItem -LiteralPath $backupRoot -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/]+_external[\\/]'
        }
)

foreach ($source in $projectBackupFiles) {
    $relative = $source.FullName.Substring($backupRoot.Length).TrimStart('\','/')
    $target = Join-Path $ProjectRoot $relative
    Copy-WithSafetyBackup $source.FullName $target $safetyRoot $ProjectRoot
}

# 2) Restore runtime configuration that lived outside ProjectRoot (Z:\...).
# Backup-File flattened external paths, so match them back by their final file name.
if (-not [string]::IsNullOrWhiteSpace($RuntimeConfigRoot) -and
    (Test-Path -LiteralPath $RuntimeConfigRoot)) {

    $externalRoot = Join-Path $backupRoot "_external"

    if (Test-Path -LiteralPath $externalRoot) {
        foreach ($leaf in @(
            "transactions.json",
            "dms-modules.json",
            "cs-CZ.json",
            "en-US.json",
            "de-DE.json"
        )) {
            $sourceCandidates = @(
                Get-ChildItem -LiteralPath $externalRoot -Recurse -File |
                    Where-Object {
                        $_.Name.EndsWith("_$leaf", [StringComparison]::OrdinalIgnoreCase) -or
                        [string]::Equals($_.Name, $leaf, [StringComparison]::OrdinalIgnoreCase)
                    }
            )

            $targetCandidates = @(
                Get-ChildItem -LiteralPath $RuntimeConfigRoot -Recurse -File -Filter $leaf
            )

            if ($sourceCandidates.Count -eq 1 -and $targetCandidates.Count -eq 1) {
                Copy-WithSafetyBackup `
                    $sourceCandidates[0].FullName `
                    $targetCandidates[0].FullName `
                    $safetyRoot `
                    $ProjectRoot
            }
            elseif ($sourceCandidates.Count -gt 0 -or $targetCandidates.Count -gt 0) {
                Write-Warning "Could not uniquely map runtime file '$leaf'. Backup candidates=$($sourceCandidates.Count), targets=$($targetCandidates.Count)."
            }
        }
    }
}

# 3) Remove files that the MES Live Overview patch added from scratch.
# If a file existed before the patch, v1.6 would contain its backup, so do not delete it.
$addedRelativePaths = @(
    "DMS\src\DMS.Core\Transactions\Handlers\MesLiveOverviewTransactionHandler.cs",
    "DMS\src\DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml",
    "DMS\src\DMS.Desktop\Views\Mes\MesLiveOverviewView.xaml.cs",
    "DMS\src\DMS.Integration.Mes\Live\MesLiveOverviewDataService.cs",
    "DMS\src\DMS.Integration.Mes\Live\MesLiveOverviewModels.cs"
)

foreach ($relative in $addedRelativePaths) {
    $current = Join-Path $ProjectRoot $relative
    $oldBackup = Join-Path $backupRoot $relative

    if ((Test-Path -LiteralPath $current) -and -not (Test-Path -LiteralPath $oldBackup)) {
        # Preserve it in the safety backup before deleting it.
        $safetyTarget = Join-Path $safetyRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $safetyTarget) -Force | Out-Null
        Copy-Item -LiteralPath $current -Destination $safetyTarget -Force

        Remove-Item -LiteralPath $current -Force
        Write-Host "  removed patch-added file: $current" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Rollback completed." -ForegroundColor Green
Write-Host "Restored from: $backupRoot" -ForegroundColor Green
Write-Host "Broken state preserved at: $safetyRoot" -ForegroundColor Green
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Close DMS if it is running."
Write-Host "  2. Visual Studio: Clean Solution."
Write-Host "  3. Delete DMS.Desktop bin/obj only if stale resources remain."
Write-Host "  4. Rebuild Solution."
Write-Host "  5. Verify CLSET, SYS01/FW transactions and Czech localization BEFORE attempting MES again."
