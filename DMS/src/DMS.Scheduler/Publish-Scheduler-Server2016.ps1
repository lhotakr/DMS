# Requires PowerShell 5.1+ on the developer computer and .NET 9 SDK.
# Publishes DMS.Scheduler as a self-contained win-x64 application.
# Does not modify DMS sources, scheduler jobs, settings, services or servers.
[CmdletBinding()]
param(
    [string]$Root = 'C:\Users\rlhotak\DevelopmentEnv\DMS',
    [string]$OutputDirectory = 'C:\temp'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).ProviderPath
    $candidates = @(
        (Join-Path $rootPath 'DMS\src\DMS.Scheduler\DMS.Scheduler.csproj'),
        (Join-Path $rootPath 'src\DMS.Scheduler\DMS.Scheduler.csproj')
    )
    $project = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $project) {
        throw "Project file not found under $rootPath. Expected DMS\src\DMS.Scheduler\DMS.Scheduler.csproj."
    }

    $dotnet = Get-Command dotnet -ErrorAction Stop
    $sdkVersions = @(& $dotnet.Source --list-sdks)
    if ($LASTEXITCODE -ne 0 -or -not ($sdkVersions | Where-Object { $_ -match '^(9|1[0-9])\.' })) {
        throw 'No .NET 9-or-newer SDK detected. Install the SDK on the development computer, not necessarily on the server.'
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $env:TEMP ('DMS_Scheduler_Server2016_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
    }
    $out = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($OutputDirectory))
    if (-not (Test-Path -LiteralPath $out)) {
        [void](New-Item -Path $out -ItemType Directory -Force)
    }
    $publish = Join-Path $out 'publish'
    if (Test-Path -LiteralPath $publish) {
        throw "Publish destination already exists: $publish . Choose a new -OutputDirectory to avoid mixing old files."
    }
    [void](New-Item -Path $publish -ItemType Directory)

    Write-Host "Project: $project"
    Write-Host 'Publishing self-contained Windows x64; trimming, single-file and ReadyToRun disabled.'
    & $dotnet.Source publish $project '-c' 'Release' '-r' 'win-x64' '--self-contained' 'true' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:PublishReadyToRun=false' '-p:UseAppHost=true' '-o' $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    foreach ($name in @('DMS.Scheduler.exe','DMS.Scheduler.runtimeconfig.json','coreclr.dll','hostfxr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) {
            throw "Publishing verification failed; missing $name in $publish"
        }
    }

    $manifest = Join-Path $out 'SHA256_manifest.txt'
    Get-ChildItem -LiteralPath $publish -File -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($publish.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$hash  $rel"
    } | Set-Content -LiteralPath $manifest -Encoding UTF8

    $zipPath = Join-Path $out 'DMS_Scheduler_win-x64_selfcontained.zip'
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw 'Output ZIP not found.' }
    Write-Host ''
    Write-Host 'PUBLISH OK' -ForegroundColor Green
    Write-Host "ZIP: $zipPath"
    Write-Host "SHA256 manifest: $manifest"
    Write-Host 'Copy the ENTIRE published package to a separate folder on the server; do not overwrite a running service.'
    Write-Host 'Use Check-Scheduler-Server2016.ps1 to collect diagnostics on the target server.'
}
catch {
    Write-Error ('PUBLISH FAILED: ' + $_.Exception.Message)
    exit 1
}
