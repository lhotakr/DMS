param(
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"

$desktopProject = Join-Path $ProjectRoot "src\DMS.Desktop\DMS.Desktop.csproj"
if (!(Test-Path $desktopProject)) {
    throw "DMS.Desktop.csproj not found. Run this script from the repository root, or pass -ProjectRoot. Expected: $desktopProject"
}

$publishProfileDir = Join-Path $ProjectRoot "src\DMS.Desktop\Properties\PublishProfiles"
New-Item -ItemType Directory -Force -Path $publishProfileDir | Out-Null

$publishProfilePath = Join-Path $publishProfileDir "DMS.Setup.pubxml"
$profileContent = @'
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishProtocol>FileSystem</PublishProtocol>
    <TargetFramework>net9.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishReadyToRun>false</PublishReadyToRun>
    <PublishDir>$(MSBuildProjectDirectory)\bin\$(Configuration)\$(TargetFramework)\$(RuntimeIdentifier)\publish\</PublishDir>
    <DeleteExistingFiles>true</DeleteExistingFiles>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>
</Project>
'@
Set-Content -Path $publishProfilePath -Value $profileContent -Encoding UTF8

Write-Host "Publish profile ready:" -ForegroundColor Green
Write-Host "  $publishProfilePath"
Write-Host ""
Write-Host "Use this value in the Setup Project Publish Items node property:" -ForegroundColor Cyan
Write-Host "  Properties\PublishProfiles\DMS.Setup.pubxml"
