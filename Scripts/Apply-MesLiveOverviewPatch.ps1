param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,

    [Parameter(Mandatory = $false)]
    [string]$RuntimeConfigRoot = '',

    [Parameter(Mandatory = $false)]
    [switch]$PreflightOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Write-Step([string]$Message) {
    Write-Host "[MES PATCH] $Message" -ForegroundColor Cyan
}

function Write-Found([string]$Label, [string]$Value) {
    Write-Host ("[MES PATCH] {0}: {1}" -f $Label, $Value) -ForegroundColor DarkCyan
}

function Test-IgnoredDiscoveryPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    # Normalize Windows separators first so stale source snapshots from old
    # patches cannot be mistaken for the active project.
    $normalized = $Path -replace '\\', '/'
    return $normalized -match '/(bin|obj|\.git|\.patch-backups|patch-backups|DMS_PatchBackups)/'
}

function Get-CodeFiles([string]$Root) {
    @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.cs' |
            Where-Object {
                -not (Test-IgnoredDiscoveryPath $_.FullName)
            }
    )
}

function Find-ProjectDirectory([string]$Root, [string]$ProjectName) {
    $projectFileName = "$ProjectName.csproj"
    $matches = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $projectFileName |
            Where-Object {
                -not (Test-IgnoredDiscoveryPath $_.FullName)
            }
    )

    if ($matches.Count -eq 0) {
        # Fallback for source trees where csproj files are temporarily absent from a patch/export.
        $directoryMatches = @(
            Get-ChildItem -LiteralPath $Root -Recurse -Directory |
                Where-Object {
                    [string]::Equals($_.Name, $ProjectName, [StringComparison]::OrdinalIgnoreCase) -and
                    -not (Test-IgnoredDiscoveryPath $_.FullName)
                }
        )

        if ($directoryMatches.Count -eq 1) {
            return $directoryMatches[0].FullName
        }

        if ($directoryMatches.Count -gt 1) {
            $list = ($directoryMatches | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
            throw "More than one $ProjectName directory was found. Candidates:$([Environment]::NewLine)$list"
        }

        throw "$ProjectName was not found anywhere under: $Root"
    }

    if ($matches.Count -gt 1) {
        # Prefer the canonical source-tree shape. This also makes -ProjectRoot
        # usable when the repository itself lives one directory below the path
        # supplied by the user (for example DMS\DMS\src\...).
        $canonical = @(
            $matches |
                Where-Object {
                    ($_.FullName -replace '\\', '/') -match ('/src/' + [regex]::Escape($ProjectName) + '/' + [regex]::Escape($projectFileName) + '$')
                }
        )

        if ($canonical.Count -eq 1) {
            return $canonical[0].Directory.FullName
        }

        $list = ($matches | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
        throw "More than one active $projectFileName was found after backup folders were excluded. Candidates:$([Environment]::NewLine)$list"
    }

    return $matches[0].Directory.FullName
}

function Get-ProjectConfigFiles([string]$Root, [string]$LeafName) {
    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root)) {
        return @()
    }

    @(
        Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $LeafName |
            Where-Object {
                -not (Test-IgnoredDiscoveryPath $_.FullName)
            }
    )
}

function Get-UniqueFiles([System.Collections.IEnumerable]$Files) {
    @($Files | Sort-Object -Property FullName -Unique)
}

function Backup-File([string]$FilePath, [string]$BackupRoot, [string]$Root) {
    if (-not (Test-Path -LiteralPath $FilePath)) {
        return
    }

    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $fullPath = [IO.Path]::GetFullPath($FilePath)
    $rootWithSeparator = $fullRoot + [IO.Path]::DirectorySeparatorChar

    if ($fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $fullPath.Substring($fullRoot.Length).TrimStart('\','/')
    }
    else {
        $safeExternal = ($fullPath -replace '[:\\/]+', '_')
        $relative = Join-Path '_external' $safeExternal
    }

    $target = Join-Path $BackupRoot $relative
    $targetDirectory = Split-Path -Parent $target
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $FilePath -Destination $target -Force
}

function Save-Json([object]$Value, [string]$Path) {
    $json = ConvertTo-Json -InputObject $Value -Depth 30
    [IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Get-CollectionProperty([object]$Object, [string[]]$Names) {
    if ($null -eq $Object) {
        return $null
    }

    foreach ($property in $Object.PSObject.Properties) {
        foreach ($name in $Names) {
            if ([string]::Equals($property.Name, $name, [StringComparison]::OrdinalIgnoreCase)) {
                return $property
            }
        }
    }

    return $null
}

function Set-JsonProperty([object]$Object, [string]$Name, [object]$Value) {
    $existing = $Object.PSObject.Properties[$Name]
    if ($null -eq $existing) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $existing.Value = $Value
    }
}

function Get-JsonPropertyValue([object]$Object, [string]$Name) {
    if ($null -eq $Object) {
        return $null
    }

    foreach ($property in $Object.PSObject.Properties) {
        if ([string]::Equals($property.Name, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $property.Value
        }
    }

    return $null
}

function Read-CollectionConfig(
    [string]$Path,
    [string[]]$ContainerNames,
    [string]$Kind) {

    $document = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $document) {
        throw "$Kind configuration is empty: $Path"
    }

    # Supported shape A: { "Transactions": [ ... ] } / { "Modules": [ ... ] }
    $containerProperty = Get-CollectionProperty $document $ContainerNames
    if ($null -ne $containerProperty) {
        return [pscustomobject]@{
            Document = $document
            Items = @($containerProperty.Value)
            ContainerPropertyName = $containerProperty.Name
            NormalizedMixedArray = $false
        }
    }

    # Supported shape B: [ ... ]
    if ($document -is [System.Array]) {
        # v1.6 could turn a wrapped config into this mixed structure:
        # [ { "Transactions": [ ... ] }, { "Code": "MES", ... } ]
        # Detect that shape and normalize it back to the original wrapper.
        $wrapperEntries = @(
            $document |
                Where-Object {
                    $null -ne (Get-CollectionProperty $_ $ContainerNames)
                }
        )

        if ($wrapperEntries.Count -eq 1) {
            $wrapper = $wrapperEntries[0]
            $wrapperProperty = Get-CollectionProperty $wrapper $ContainerNames
            $items = @($wrapperProperty.Value)

            foreach ($entry in @($document)) {
                if ([object]::ReferenceEquals($entry, $wrapper)) {
                    continue
                }

                if ($null -ne $entry.PSObject.Properties['Code']) {
                    $items += $entry
                }
            }

            return [pscustomobject]@{
                Document = $wrapper
                Items = @($items)
                ContainerPropertyName = $wrapperProperty.Name
                NormalizedMixedArray = $true
            }
        }

        return [pscustomobject]@{
            Document = $document
            Items = @($document)
            ContainerPropertyName = $null
            NormalizedMixedArray = $false
        }
    }

    # Supported shape C: a single transaction/module object.
    if ($null -ne $document.PSObject.Properties['Code']) {
        return [pscustomobject]@{
            Document = $document
            Items = @($document)
            ContainerPropertyName = $null
            NormalizedMixedArray = $false
        }
    }

    throw "Unsupported $Kind configuration JSON shape: $Path"
}

function Save-CollectionConfig(
    [object]$Config,
    [System.Collections.IEnumerable]$Items,
    [string]$Path) {

    $normalizedItems = @($Items)

    if (-not [string]::IsNullOrWhiteSpace([string]$Config.ContainerPropertyName)) {
        Set-JsonProperty $Config.Document ([string]$Config.ContainerPropertyName) $normalizedItems
        Save-Json $Config.Document $Path
        return
    }

    Save-Json $normalizedItems $Path
}

function Upsert-Transaction([string]$Path) {
    $config = Read-CollectionConfig $Path @('Transactions','TransactionDefinitions','Items') 'transaction'
    $items = @($config.Items)

    if ($config.NormalizedMixedArray) {
        Write-Found 'Repairing v1.6 mixed transaction JSON' $Path
    }

    $existing = $items |
        Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ 'Code'), 'MES', [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1

    if ($null -eq $existing) {
        $items += [pscustomobject]@{
            Code = 'MES'
            Name = 'Přehled výroby'
            Module = 'MES'
            Description = 'Živý read-only přehled pracovišť, směn a stavů výroby z FASTEC SQL.'
            HandlerKey = 'MesLiveOverview'
            RequiresArticleNumber = $false
            IsActive = $true
            Roles = @()
        }
    }
    else {
        Set-JsonProperty $existing 'Name' 'Přehled výroby'
        Set-JsonProperty $existing 'Module' 'MES'
        Set-JsonProperty $existing 'Description' 'Živý read-only přehled pracovišť, směn a stavů výroby z FASTEC SQL.'
        Set-JsonProperty $existing 'HandlerKey' 'MesLiveOverview'
        Set-JsonProperty $existing 'RequiresArticleNumber' $false
        Set-JsonProperty $existing 'IsActive' $true

        if ($null -eq $existing.PSObject.Properties['Roles'] -or $null -eq (Get-JsonPropertyValue $existing 'Roles')) {
            Set-JsonProperty $existing 'Roles' @()
        }
    }

    Save-CollectionConfig $config $items $Path
}

function Upsert-Module([string]$Path) {
    $config = Read-CollectionConfig $Path @('Modules','ModuleDefinitions','Items') 'module'
    $items = @($config.Items)

    if ($config.NormalizedMixedArray) {
        Write-Found 'Repairing v1.6 mixed module JSON' $Path
    }

    $existing = $items |
        Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ 'Code'), 'MES', [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1

    if ($null -eq $existing) {
        $items += [pscustomobject]@{
            Code = 'MES'
            Name = 'MES'
            Description = 'MES integrace, monitoring, reporting a živý přehled výroby.'
            SortOrder = 80
            IsActive = $true
        }
    }
    else {
        Set-JsonProperty $existing 'IsActive' $true
    }

    Save-CollectionConfig $config $items $Path
}

function Merge-Localization([string]$TargetPath, [string]$PatchPath) {
    $target = Get-Content -LiteralPath $TargetPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $patch = Get-Content -LiteralPath $PatchPath -Raw -Encoding UTF8 | ConvertFrom-Json

    foreach ($property in $patch.PSObject.Properties) {
        $existing = $target.PSObject.Properties[$property.Name]
        if ($null -eq $existing) {
            $target | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value
        }
        else {
            $existing.Value = $property.Value
        }
    }

    Save-Json $target $TargetPath
}

function Copy-ProjectPayload(
    [string]$PatchProjectDirectory,
    [string]$TargetProjectDirectory,
    [string]$BackupRoot,
    [string]$ProjectRoot) {

    if (-not (Test-Path -LiteralPath $PatchProjectDirectory)) {
        return
    }

    foreach ($source in Get-ChildItem -LiteralPath $PatchProjectDirectory -Recurse -File) {
        $relative = $source.FullName.Substring($PatchProjectDirectory.Length).TrimStart('\','/')
        $target = Join-Path $TargetProjectDirectory $relative

        if (Test-Path -LiteralPath $target) {
            Backup-File $target $BackupRoot $ProjectRoot
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $target -Force
    }
}

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $ProjectRoot)) {
    throw "ProjectRoot does not exist: $ProjectRoot"
}

if (-not [string]::IsNullOrWhiteSpace($RuntimeConfigRoot)) {
    $RuntimeConfigRoot = [IO.Path]::GetFullPath($RuntimeConfigRoot)
}

$PatchRoot = Split-Path -Parent $PSScriptRoot

Write-Step 'Discovering actual DMS project layout...'
$desktopRoot = Find-ProjectDirectory $ProjectRoot 'DMS.Desktop'
$coreRoot = Find-ProjectDirectory $ProjectRoot 'DMS.Core'
$mesIntegrationRoot = Find-ProjectDirectory $ProjectRoot 'DMS.Integration.Mes'

Write-Found 'DMS.Desktop' $desktopRoot
Write-Found 'DMS.Core' $coreRoot
Write-Found 'DMS.Integration.Mes' $mesIntegrationRoot

# IMPORTANT: do not assume MainWindow.xaml.cs, MainWindow.*.cs, a partial class,
# or even the shell file name. Discover the active shell code by behavior.
$desktopSourceFiles = @(Get-CodeFiles $desktopRoot)
if ($desktopSourceFiles.Count -eq 0) {
    throw "No C# source files were found under: $desktopRoot"
}

$sourceIndex = @()
foreach ($candidate in $desktopSourceFiles) {
    $candidateText = Get-Content -LiteralPath $candidate.FullName -Raw -Encoding UTF8
    $sourceIndex += [pscustomobject]@{
        File = $candidate
        Text = $candidateText
    }
}

$handlerCandidates = @(
    $sourceIndex |
        Where-Object {
            $_.Text -match 'new\s+ITransactionHandler\s*\[\s*\]' -and
            $_.Text -match 'InitializeTransactions|TransactionDispatcher'
        }
)

if ($handlerCandidates.Count -eq 0) {
    $fallbackHandlerCandidates = @(
        $sourceIndex |
            Where-Object {
                $_.Text -match 'new\s+ITransactionHandler\s*\[\s*\]'
            }
    )
    $handlerCandidates = $fallbackHandlerCandidates
}

if ($handlerCandidates.Count -eq 0) {
    throw "Could not find the transaction handler registration anywhere under: $desktopRoot"
}

$handlerTarget = $handlerCandidates |
    Sort-Object -Property @{ Expression = {
        $score = 0
        if ($_.Text -match 'MES06|MesReporting') { $score += 20 }
        if ($_.Text -match 'InitializeTransactions') { $score += 10 }
        if ($_.Text -match 'SimpleMessageTransactionHandler') { $score += 3 }
        $score
    }; Descending = $true } |
    Select-Object -First 1

$switchCandidates = @(
    $sourceIndex |
        Where-Object {
            $_.Text -match 'switch\s*\(\s*result\.TransactionCode\s*\)'
        }
)

if ($switchCandidates.Count -eq 0) {
    throw "Could not find switch(result.TransactionCode) anywhere under: $desktopRoot"
}

$switchTarget = $switchCandidates |
    Sort-Object -Property @{ Expression = {
        $score = 0
        if ($_.Text -match 'case\s+"MES06"\s*:') { $score += 50 }
        if ($_.Text -match 'RenderMesReporting') { $score += 25 }
        if ($_.Text -match 'RenderTransactionResult') { $score += 10 }
        $score
    }; Descending = $true } |
    Select-Object -First 1

$handlerFile = $handlerTarget.File.FullName
$switchFile = $switchTarget.File.FullName

Write-Found 'Transaction handler registration' $handlerFile
Write-Found 'Transaction render switch' $switchFile

$renderMethodMatch = [regex]::Match(
    $switchTarget.Text,
    '(?m)^\s*(?:(?:private|internal|public|protected)\s+)?void\s+RenderTransactionResult\s*\(')

if (-not $renderMethodMatch.Success) {
    throw "RenderTransactionResult method was not found in the selected render file: $switchFile"
}

# Verify the shell members used by the injected render method exist somewhere in the
# desktop shell source. They may live in another partial file, so search the whole project.
$desktopAllText = ($sourceIndex | ForEach-Object { $_.Text }) -join [Environment]::NewLine
foreach ($requiredAnchor in @('WorkspacePanel', '_appSettings.ConfigurationRootPath', '_logger', '_currentUser', 'ResetWorkspaceScroll', 'T(')) {
    if ($desktopAllText -notmatch [regex]::Escape($requiredAnchor)) {
        throw "Required shell member '$requiredAnchor' was not found in DMS.Desktop sources. No files were changed."
    }
}

$configRoots = @($ProjectRoot)
if (-not [string]::IsNullOrWhiteSpace($RuntimeConfigRoot)) {
    if (-not (Test-Path -LiteralPath $RuntimeConfigRoot)) {
        throw "RuntimeConfigRoot does not exist: $RuntimeConfigRoot"
    }
    $configRoots += $RuntimeConfigRoot
}

$transactionCandidates = @()
$moduleCandidates = @()
$localizationTargets = @{}
foreach ($culture in @('cs-CZ','en-US','de-DE')) {
    $localizationTargets[$culture] = @()
}

foreach ($root in $configRoots) {
    $transactionCandidates += Get-ProjectConfigFiles $root 'transactions.json'
    $moduleCandidates += Get-ProjectConfigFiles $root 'dms-modules.json'

    foreach ($culture in @('cs-CZ','en-US','de-DE')) {
        $localizationTargets[$culture] += Get-ProjectConfigFiles $root "$culture.json"
    }
}

$transactionFiles = @(Get-UniqueFiles $transactionCandidates)
$moduleFiles = @(Get-UniqueFiles $moduleCandidates)
foreach ($culture in @('cs-CZ','en-US','de-DE')) {
    $localizationTargets[$culture] = @(Get-UniqueFiles $localizationTargets[$culture])
}

if ($transactionFiles.Count -eq 0) {
    throw 'No transactions.json was found. Pass -RuntimeConfigRoot when the active DMS configuration lives outside the source tree.'
}
if ($moduleFiles.Count -eq 0) {
    throw 'No dms-modules.json was found. Pass -RuntimeConfigRoot when the active DMS configuration lives outside the source tree.'
}
foreach ($culture in @('cs-CZ','en-US','de-DE')) {
    if ($localizationTargets[$culture].Count -eq 0) {
        throw "No $culture.json localization dictionary was found. Pass -RuntimeConfigRoot when localization lives outside the source tree."
    }
}

Write-Step 'Preflight passed. Resolved all source/config anchors.'
Write-Found 'transactions.json files' ([string]$transactionFiles.Count)
Write-Found 'dms-modules.json files' ([string]$moduleFiles.Count)
Write-Found 'cs-CZ dictionaries' ([string]$localizationTargets['cs-CZ'].Count)
Write-Found 'en-US dictionaries' ([string]$localizationTargets['en-US'].Count)
Write-Found 'de-DE dictionaries' ([string]$localizationTargets['de-DE'].Count)

if ($PreflightOnly) {
    Write-Host ''
    Write-Host 'Preflight-only mode completed. No files were changed.' -ForegroundColor Green
    exit 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path (Split-Path -Parent $ProjectRoot) "DMS_PatchBackups\MES_LiveOverview_v1_8_$timestamp"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
Write-Found 'Backup root' $backupRoot

Backup-File $handlerFile $backupRoot $ProjectRoot
if (-not [string]::Equals($switchFile, $handlerFile, [StringComparison]::OrdinalIgnoreCase)) {
    Backup-File $switchFile $backupRoot $ProjectRoot
}

foreach ($file in $transactionFiles) {
    Backup-File $file.FullName $backupRoot $ProjectRoot
}
foreach ($file in $moduleFiles) {
    Backup-File $file.FullName $backupRoot $ProjectRoot
}
foreach ($culture in $localizationTargets.Keys) {
    foreach ($file in $localizationTargets[$culture]) {
        Backup-File $file.FullName $backupRoot $ProjectRoot
    }
}

Write-Step 'Copying source files into the discovered project directories...'
Copy-ProjectPayload `
    (Join-Path $PatchRoot 'src\DMS.Core') `
    $coreRoot `
    $backupRoot `
    $ProjectRoot

Copy-ProjectPayload `
    (Join-Path $PatchRoot 'src\DMS.Desktop') `
    $desktopRoot `
    $backupRoot `
    $ProjectRoot

Copy-ProjectPayload `
    (Join-Path $PatchRoot 'src\DMS.Integration.Mes') `
    $mesIntegrationRoot `
    $backupRoot `
    $ProjectRoot

Write-Step 'Patching transaction handler registration...'
$handlerText = Get-Content -LiteralPath $handlerFile -Raw -Encoding UTF8
if ($handlerText -notmatch 'MesLiveOverviewTransactionHandler\s*\(') {
    $handlerAnchor = [regex]::Match(
        $handlerText,
        'new\s+ITransactionHandler\s*\[\s*\]\s*\{')

    if (-not $handlerAnchor.Success) {
        throw 'Could not insert MES live transaction handler after preflight. Restore the timestamped backup.'
    }

    $insertAt = $handlerAnchor.Index + $handlerAnchor.Length
    $handlerLine = "`r`n            new DMS.Core.Transactions.Handlers.MesLiveOverviewTransactionHandler(),"
    $handlerText = $handlerText.Insert($insertAt, $handlerLine)

    [IO.File]::WriteAllText(
        $handlerFile,
        $handlerText,
        [Text.UTF8Encoding]::new($false))
}

Write-Step 'Patching transaction render switch and render method in-place...'
$switchText = Get-Content -LiteralPath $switchFile -Raw -Encoding UTF8

if ($switchText -notmatch 'case\s+"MES"\s*:') {
    $switchStart = [regex]::Match(
        $switchText,
        'switch\s*\(\s*result\.TransactionCode\s*\)\s*\{')

    if (-not $switchStart.Success) {
        throw 'Could not locate the transaction render switch after preflight. Restore the timestamped backup.'
    }

    $switchBody = $switchText.Substring($switchStart.Index)
    $mes06Match = [regex]::Match($switchBody, '(?m)^\s*case\s+"MES06"\s*:')

    if ($mes06Match.Success) {
        $insertAt = $switchStart.Index + $mes06Match.Index
    }
    else {
        $defaultMatch = [regex]::Match($switchBody, '(?m)^\s*default\s*:')
        if (-not $defaultMatch.Success) {
            throw 'Could not locate MES06 or default case in the transaction render switch. Restore the timestamped backup.'
        }
        $insertAt = $switchStart.Index + $defaultMatch.Index
    }

    $caseText = "            case `"MES`":`r`n                RenderMesLiveOverview();`r`n                break;`r`n`r`n"
    $switchText = $switchText.Insert($insertAt, $caseText)
}

if ($switchText -notmatch 'void\s+RenderMesLiveOverview\s*\(') {
    $renderMethodMatch = [regex]::Match(
        $switchText,
        '(?m)^\s*(?:(?:private|internal|public|protected)\s+)?void\s+RenderTransactionResult\s*\(')

    if (-not $renderMethodMatch.Success) {
        throw 'Could not locate RenderTransactionResult for MES render method insertion. Restore the timestamped backup.'
    }

    $methodText = @'
    private void RenderMesLiveOverview()
    {
        WorkspacePanel.Children.Clear();

        WorkspacePanel.Children.Add(
            new DMS.Desktop.Views.Mes.MesLiveOverviewView(
                _appSettings.ConfigurationRootPath,
                _logger,
                _currentUser.DisplayName,
                translate: key => T(key)));

        ResetWorkspaceScroll();
    }

'@

    $switchText = $switchText.Insert($renderMethodMatch.Index, $methodText)
}

[IO.File]::WriteAllText(
    $switchFile,
    $switchText,
    [Text.UTF8Encoding]::new($false))

Write-Step 'Updating transaction/module configuration copies...'
foreach ($file in $transactionFiles) {
    Upsert-Transaction $file.FullName
}
foreach ($file in $moduleFiles) {
    Upsert-Module $file.FullName
}

Write-Step 'Merging Czech/English/German localization...'
foreach ($culture in @('cs-CZ','en-US','de-DE')) {
    $patchFile = Join-Path $PatchRoot "Patch\Localization\$culture.MES.upsert.json"
    foreach ($target in $localizationTargets[$culture]) {
        Merge-Localization $target.FullName $patchFile
    }
}

Write-Step 'Postflight validation...'
$handlerText = Get-Content -LiteralPath $handlerFile -Raw -Encoding UTF8
$switchText = Get-Content -LiteralPath $switchFile -Raw -Encoding UTF8

if ($handlerText -notmatch 'MesLiveOverviewTransactionHandler\s*\(') {
    throw 'Postflight failed: MesLiveOverviewTransactionHandler is not registered.'
}
if ($switchText -notmatch 'case\s+"MES"\s*:') {
    throw 'Postflight failed: MES render case is missing.'
}
if ($switchText -notmatch 'void\s+RenderMesLiveOverview\s*\(') {
    throw 'Postflight failed: RenderMesLiveOverview method is missing.'
}

$expectedFiles = @(
    (Join-Path $coreRoot 'Transactions\Handlers\MesLiveOverviewTransactionHandler.cs'),
    (Join-Path $desktopRoot 'Views\Mes\MesLiveOverviewView.xaml'),
    (Join-Path $desktopRoot 'Views\Mes\MesLiveOverviewView.xaml.cs'),
    (Join-Path $mesIntegrationRoot 'Live\MesLiveOverviewDataService.cs'),
    (Join-Path $mesIntegrationRoot 'Live\MesLiveOverviewModels.cs')
)
foreach ($expected in $expectedFiles) {
    if (-not (Test-Path -LiteralPath $expected)) {
        throw "Postflight failed: expected source file is missing: $expected"
    }
}

foreach ($file in $transactionFiles) {
    $transactionConfig = Read-CollectionConfig $file.FullName @('Transactions','TransactionDefinitions','Items') 'transaction'
    $items = @($transactionConfig.Items)
    $mes = $items |
        Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ 'Code'), 'MES', [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1

    if ($null -eq $mes -or
        -not [string]::Equals([string](Get-JsonPropertyValue $mes 'HandlerKey'), 'MesLiveOverview', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Transaction MES postflight validation failed: $($file.FullName)"
    }
}

foreach ($file in $moduleFiles) {
    $moduleConfig = Read-CollectionConfig $file.FullName @('Modules','ModuleDefinitions','Items') 'module'
    $items = @($moduleConfig.Items)
    $mes = $items |
        Where-Object {
            [string]::Equals([string](Get-JsonPropertyValue $_ 'Code'), 'MES', [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1

    if ($null -eq $mes) {
        throw "Module MES postflight validation failed: $($file.FullName)"
    }
}

foreach ($culture in @('cs-CZ','en-US','de-DE')) {
    foreach ($target in $localizationTargets[$culture]) {
        $dictionary = Get-Content -LiteralPath $target.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $dictionary.PSObject.Properties['Transaction.MES.Name'] -or
            $null -eq $dictionary.PSObject.Properties['MES.Title']) {
            throw "Localization MES postflight validation failed: $($target.FullName)"
        }
    }
}

Write-Step 'Postflight validation passed.'
Write-Host ''
Write-Host 'Patch applied successfully.' -ForegroundColor Green
Write-Host "Backup: $backupRoot" -ForegroundColor Green
Write-Host ''
Write-Host 'Next: Clean + Rebuild DMS in Visual Studio, then run transaction MES.' -ForegroundColor Green
