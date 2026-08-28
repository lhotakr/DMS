param(
    [string]$ProjectRoot = (Get-Location).Path,
    [switch]$Clean,
    [switch]$SkipReleaseGate
)

$ErrorActionPreference = "Stop"

$desktopProject = Join-Path $ProjectRoot "src\DMS.Desktop\DMS.Desktop.csproj"
if (!(Test-Path $desktopProject)) {
    throw "DMS.Desktop.csproj not found. Run this script from the repository root, or pass -ProjectRoot. Expected: $desktopProject"
}

if (-not $SkipReleaseGate) {
    $releaseGateScript = Join-Path $ProjectRoot "Scripts\Invoke-DMS-ReleaseGate.ps1"
    if (!(Test-Path $releaseGateScript)) { throw "Release gate script not found: $releaseGateScript" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $releaseGateScript -ProjectRoot $ProjectRoot
    if ($LASTEXITCODE -ne 0) { throw "DMS release gate blocked the publish. Resolve FW11 findings. -SkipReleaseGate is only for intentional diagnostic builds." }
}
else { Write-Warning "Release gate skipped explicitly. Do not use this for production without documented justification." }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectRoot "Scripts\Prepare-DMS-VS-SetupProject.ps1") -ProjectRoot $ProjectRoot

if ($Clean) {
    dotnet clean $desktopProject -c Release
}

dotnet restore $desktopProject
dotnet publish $desktopProject -c Release -p:PublishProfile=DMS.Setup

$publishDir = Join-Path $ProjectRoot "src\DMS.Desktop\bin\Release\net9.0-windows\win-x64\publish"
Write-Host ""
Write-Host "Publish output for Setup Project:" -ForegroundColor Green
Write-Host "  $publishDir"
