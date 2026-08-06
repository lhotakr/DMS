param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$DefaultMesDevicesFilePath = "\\10.131.10.5\FISData\devices.txt"
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$message) {
    Write-Host "[MES-REF] $message" -ForegroundColor Cyan
}

function Copy-FileSafe([string]$source, [string]$target) {
    $targetDir = Split-Path -Parent $target
    if (-not [string]::IsNullOrWhiteSpace($targetDir) -and -not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    $sourceFull = [System.IO.Path]::GetFullPath($source)
    $targetFull = [System.IO.Path]::GetFullPath($target)

    if ($sourceFull.Equals($targetFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Step "Skipped self-copy $target"
        return
    }

    Copy-Item -LiteralPath $source -Destination $target -Force
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$patchRoot = Split-Path -Parent $scriptRoot
$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)

Write-Step "Project root: $ProjectRoot"
Write-Step "Patch root:   $patchRoot"

$copyList = @(
    "DMS.csproj",
    "README_MES_INTEGRATION_REFACTOR.md",
    "src\DMS.Integration.Mes\DMS.Integration.Mes.csproj",
    "src\DMS.Integration.Mes\Models\MesCommunicationSettings.cs",
    "src\DMS.Integration.Mes\Models\MesDevice.cs",
    "src\DMS.Integration.Mes\Models\MesMonitorSnapshot.cs",
    "src\DMS.Integration.Mes\Models\MesProbeResult.cs",
    "src\DMS.Integration.Mes\Services\MesCommunicationSettingsService.cs",
    "src\DMS.Integration.Mes\Services\MesDeviceFileService.cs",
    "src\DMS.Integration.Mes\Services\MesProbeService.cs",
    "src\DMS.Desktop\Models\MesDeviceEditRow.cs",
    "src\DMS.Desktop\Models\MesDeviceStatusRow.cs",
    "src\DMS.Desktop\Views\Mes\MesCommunicationSettingsView.xaml.cs",
    "src\DMS.Desktop\Views\Mes\MesDeviceEditorView.xaml.cs",
    "src\DMS.Desktop\Views\Mes\MesDeviceMonitorView.xaml.cs",
    "src\DMS.Desktop\Views\MainWindow.Render.Mes.cs"
)

foreach ($relative in $copyList) {
    $source = Join-Path $patchRoot $relative
    $target = Join-Path $ProjectRoot $relative
    if (Test-Path $source) {
        Copy-FileSafe $source $target
        Write-Step "Updated $relative"
    }
}

$obsolete = @(
    "src\DMS.Desktop\Models\MesCommunicationSettings.cs",
    "src\DMS.Desktop\Models\MesDevice.cs",
    "src\DMS.Desktop\Models\MesMonitorSnapshot.cs",
    "src\DMS.Desktop\Models\MesProbeResult.cs",
    "src\DMS.Desktop\Services\MesCommunicationSettingsService.cs",
    "src\DMS.Desktop\Services\MesDeviceFileService.cs",
    "src\DMS.Desktop\Services\MesProbeService.cs",
    "src\DMS.Integration.Mes\Class1.cs"
)

foreach ($relative in $obsolete) {
    $target = Join-Path $ProjectRoot $relative
    if (Test-Path $target) {
        Remove-Item -LiteralPath $target -Force
        Write-Step "Removed obsolete $relative"
    }
}

# Preserve an existing MES00 settings file; create/update only when missing.
$settingsPath = Join-Path $ProjectRoot "Config\mes-communication-settings.json"
if (-not (Test-Path $settingsPath)) {
    $settingsDir = Split-Path -Parent $settingsPath
    if (-not (Test-Path $settingsDir)) {
        New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    }

    $json = [ordered]@{
        isMonitoringEnabled = $true
        pingTimeoutMs = 1200
        maxParallelism = 16
        autoRefreshSeconds = 60
        devicesFilePath = $DefaultMesDevicesFilePath
        enableMachineUnlockSignal = $false
        unlockProvider = "None"
        gatewayHost = ""
        gatewayPort = 0
        sharedHandshakeFolder = ""
        requireOperatorConfirmation = $true
        setupOkValidMinutes = 480
    } | ConvertTo-Json -Depth 10

    Set-Content -LiteralPath $settingsPath -Value $json -Encoding UTF8
    Write-Step "Created Config\mes-communication-settings.json"
}
else {
    Write-Step "Kept existing Config\mes-communication-settings.json"
}

Write-Step "Done. Recommended check: dotnet build .\src\DMS.Desktop\DMS.Desktop.csproj"
