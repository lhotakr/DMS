param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ConfigurationRoot = "",
    [switch]$NoWriteTests
)

$ErrorActionPreference = "Stop"
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Severity,
        [string]$Check,
        [string]$Details
    )

    $results.Add([pscustomobject]@{
        Severity = $Severity
        Check = $Check
        Details = $Details
    })

    Write-Host ("[{0}] {1}: {2}" -f $Severity, $Check, $Details)
}

function Read-Json {
    param(
        [string]$Path,
        [bool]$Required,
        [string]$Check
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Add-Result `
            -Severity $(if ($Required) { "ERROR" } else { "WARNING" }) `
            -Check $Check `
            -Details "Missing: $Path"
        return $null
    }

    try {
        $value = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        Add-Result -Severity "OK" -Check $Check -Details $Path
        return $value
    }
    catch {
        Add-Result `
            -Severity $(if ($Required) { "ERROR" } else { "WARNING" }) `
            -Check $Check `
            -Details ("Invalid JSON: {0} | {1}" -f $Path, $_.Exception.Message)
        return $null
    }
}

function Test-DmsDirectory {
    param(
        [string]$Path,
        [string]$Check,
        [bool]$Required,
        [bool]$RequireWrite
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Container)) {
        Add-Result `
            -Severity $(if ($Required) { "ERROR" } else { "WARNING" }) `
            -Check $Check `
            -Details "Directory unavailable: $Path"
        return
    }

    if (-not $RequireWrite -or $NoWriteTests) {
        Add-Result -Severity "OK" -Check $Check -Details $Path
        return
    }

    $probe = Join-Path $Path (".dms-fw11-{0}.tmp" -f [guid]::NewGuid().ToString("N"))

    try {
        [System.IO.File]::WriteAllText($probe, "DMS FW11 write test")
        Remove-Item -LiteralPath $probe -Force
        Add-Result -Severity "OK" -Check $Check -Details "Writable: $Path"
    }
    catch {
        Add-Result `
            -Severity "ERROR" `
            -Check $Check `
            -Details ("Not writable: {0} | {1}" -f $Path, $_.Exception.Message)
    }
    finally {
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    }
}

$desktopProject = Join-Path $ProjectRoot "src\DMS.Desktop\DMS.Desktop.csproj"
if (Test-Path -LiteralPath $desktopProject) {
    Add-Result -Severity "OK" -Check "Desktop project" -Details $desktopProject
}
else {
    Add-Result -Severity "CRITICAL" -Check "Desktop project" -Details "Missing: $desktopProject"
}

$appSettingsPath = Join-Path $ProjectRoot "src\DMS.Desktop\Config\appsettings.json"
$appSettings = Read-Json -Path $appSettingsPath -Required $true -Check "appsettings.json"

if ($null -ne $appSettings) {
    if ([string]::IsNullOrWhiteSpace($ConfigurationRoot)) {
        $ConfigurationRoot = [string]$appSettings.ConfigurationRootPath
    }

    $environment = [string]$appSettings.Environment
    Add-Result `
        -Severity $(if ($environment -ieq "PROD") { "OK" } else { "WARNING" }) `
        -Check "Environment" `
        -Details ("Configured: {0}" -f $environment)

    $dataRoot = [System.IO.Path]::GetFullPath((Join-Path $ConfigurationRoot ".."))

    Test-DmsDirectory -Path $ConfigurationRoot -Check "Configuration root" -Required $true -RequireWrite $true
    Test-DmsDirectory -Path $dataRoot -Check "Data root" -Required $true -RequireWrite $true
    Test-DmsDirectory -Path ([string]$appSettings.DocumentsRootPath) -Check "Documents root" -Required $true -RequireWrite $true
    Test-DmsDirectory -Path ([string]$appSettings.LogsRootPath) -Check "Logs root" -Required $true -RequireWrite $true

    $transactions = Read-Json -Path (Join-Path $ConfigurationRoot "transactions.json") -Required $true -Check "Transactions"
    $modules = Read-Json -Path (Join-Path $ConfigurationRoot "dms-modules.json") -Required $true -Check "Modules"
    $roles = Read-Json -Path (Join-Path $ConfigurationRoot "dms-roles.json") -Required $true -Check "Roles"
    $users = Read-Json -Path (Join-Path $ConfigurationRoot "users.json") -Required $true -Check "Users"

    if ($null -ne $transactions) {
        $duplicateTransactions = @(
            @($transactions) |
                Group-Object -Property Code |
                Where-Object { $_.Count -gt 1 } |
                ForEach-Object { $_.Name }
        )

        Add-Result `
            -Severity $(if ($duplicateTransactions.Count -gt 0) { "ERROR" } else { "OK" }) `
            -Check "Duplicate transactions" `
            -Details $(if ($duplicateTransactions.Count -gt 0) { $duplicateTransactions -join ", " } else { "None" })
    }

    if ($null -ne $transactions -and $null -ne $modules) {
        # Runtime module identity accepts both technical Code and configured Name.
        # This mirrors MainWindow/SYS13 and prevents false release blockers for
        # legacy transactions that still store values such as "Administrace".
        $moduleReferences = @{}

        foreach ($module in @($modules)) {
            $code = [string]$module.Code
            $name = [string]$module.Name

            if (-not [string]::IsNullOrWhiteSpace($code)) {
                $moduleReferences[$code.Trim()] = $true
            }

            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $moduleReferences[$name.Trim()] = $true
            }
        }

        $unknownModules = New-Object System.Collections.Generic.List[string]

        foreach ($transaction in @($transactions)) {
            $moduleValue = [string]$transaction.Module

            if (-not [string]::IsNullOrWhiteSpace($moduleValue) -and
                -not $moduleReferences.ContainsKey($moduleValue.Trim())) {
                $unknownModules.Add(
                    ("{0}->{1}" -f
                        [string]$transaction.Code,
                        $moduleValue))
            }
        }

        Add-Result `
            -Severity $(if ($unknownModules.Count -gt 0) { "ERROR" } else { "OK" }) `
            -Check "Transaction module references" `
            -Details $(if ($unknownModules.Count -gt 0) { $unknownModules -join ", " } else { "All valid" })
    }

    if ($null -ne $roles) {
        $roleCodes = @{}
        foreach ($role in @($roles)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$role.Code)) {
                $roleCodes[[string]$role.Code] = $role
            }
        }

        Add-Result `
            -Severity $(if ($roleCodes.ContainsKey("DMS_ADMIN")) { "OK" } else { "CRITICAL" }) `
            -Check "DMS_ADMIN role" `
            -Details $(if ($roleCodes.ContainsKey("DMS_ADMIN")) { "Present" } else { "Missing" })

        if ($null -ne $users) {
            $admins = @(
                @($users) |
                    Where-Object {
                        $_.IsActive -ne $false -and
                        @($_.Roles) -contains "DMS_ADMIN"
                    }
            )

            Add-Result `
                -Severity $(if ($admins.Count -gt 0) { "OK" } else { "CRITICAL" }) `
                -Check "Active administrator" `
                -Details ("Count={0}" -f $admins.Count)

            $unknownUserRoles = New-Object System.Collections.Generic.List[string]
            foreach ($user in @($users)) {
                if ($user.IsActive -eq $false) { continue }
                foreach ($roleCode in @($user.Roles)) {
                    if (-not $roleCodes.ContainsKey([string]$roleCode)) {
                        $unknownUserRoles.Add(("{0}->{1}" -f [string]$user.WindowsLogin, [string]$roleCode))
                    }
                }
            }

            Add-Result `
                -Severity $(if ($unknownUserRoles.Count -gt 0) { "ERROR" } else { "OK" }) `
                -Check "User role references" `
                -Details $(if ($unknownUserRoles.Count -gt 0) { $unknownUserRoles -join ", " } else { "All valid" })
        }

        if ($null -ne $transactions) {
            $unknownTransactionRoles = New-Object System.Collections.Generic.List[string]
            foreach ($transaction in @($transactions)) {
                foreach ($roleCode in @($transaction.Roles)) {
                    if (-not $roleCodes.ContainsKey([string]$roleCode)) {
                        $unknownTransactionRoles.Add(("{0}->{1}" -f [string]$transaction.Code, [string]$roleCode))
                    }
                }
            }

            Add-Result `
                -Severity $(if ($unknownTransactionRoles.Count -gt 0) { "ERROR" } else { "OK" }) `
                -Check "Transaction role references" `
                -Details $(if ($unknownTransactionRoles.Count -gt 0) { $unknownTransactionRoles -join ", " } else { "All valid" })
        }
    }

    $localizationRoot = Join-Path $ConfigurationRoot "Localization"
    $localizationIndex = Read-Json -Path (Join-Path $localizationRoot "localization.index.json") -Required $true -Check "Localization index"

    if ($null -ne $localizationIndex) {
        $defaultCulture = [string]$localizationIndex.DefaultCulture
        $referenceDictionary = Read-Json `
            -Path (Join-Path $localizationRoot ($defaultCulture + ".json")) `
            -Required $true `
            -Check ("Localization {0}" -f $defaultCulture)

        if ($null -ne $referenceDictionary) {
            $referenceKeys = @($referenceDictionary.PSObject.Properties.Name)

            foreach ($cultureEntry in @($localizationIndex.SupportedCultures)) {
                $culture = [string]$cultureEntry.Culture
                if ([string]::IsNullOrWhiteSpace($culture)) { continue }

                $dictionary = Read-Json `
                    -Path (Join-Path $localizationRoot ($culture + ".json")) `
                    -Required $true `
                    -Check ("Localization {0}" -f $culture)

                if ($null -eq $dictionary) { continue }

                $keys = @($dictionary.PSObject.Properties.Name)
                $missing = @($referenceKeys | Where-Object { $_ -notin $keys })

                Add-Result `
                    -Severity $(if ($missing.Count -gt 0) { "ERROR" } else { "OK" }) `
                    -Check ("Missing localization keys {0}" -f $culture) `
                    -Details $(if ($missing.Count -gt 0) {
                        "{0} missing: {1}" -f $missing.Count, (($missing | Select-Object -First 20) -join ", ")
                    } else {
                        "None"
                    })
            }
        }
    }

    $articlesPath = [string]$appSettings.ArticlesDataPath
    Add-Result `
        -Severity $(if (-not [string]::IsNullOrWhiteSpace($articlesPath) -and
                         (Test-Path -LiteralPath $articlesPath -PathType Leaf)) { "OK" } else { "ERROR" }) `
        -Check "Articles cache" `
        -Details $articlesPath
}

$critical = @($results | Where-Object { $_.Severity -eq "CRITICAL" }).Count
$errors = @($results | Where-Object { $_.Severity -eq "ERROR" }).Count
$warnings = @($results | Where-Object { $_.Severity -eq "WARNING" }).Count
$passed = @($results | Where-Object { $_.Severity -eq "OK" }).Count

$rqi = [math]::Max(0, 100 - ($critical * 25) - ($errors * 5) - ($warnings * 0.5))
$verdict = if ($critical -gt 0 -or $errors -gt 0) {
    "NOT READY"
}
elseif ($warnings -gt 0) {
    "READY WITH WARNINGS"
}
else {
    "READY"
}

$artifactRoot = Join-Path $ProjectRoot "artifacts\release-gate"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$reportPath = Join-Path $artifactRoot ("dms-release-gate-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

[pscustomobject]@{
    GeneratedAt = (Get-Date).ToString("o")
    Verdict = $verdict
    ReleaseQualityIndex = $rqi
    Critical = $critical
    Errors = $errors
    Warnings = $warnings
    Passed = $passed
    Results = $results
} |
    ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "DMS RELEASE GATE" -ForegroundColor Cyan
Write-Host ("Verdict : {0}" -f $verdict)
Write-Host ("RQI     : {0:0.0} / 100" -f $rqi)
Write-Host ("Critical: {0} | Errors: {1} | Warnings: {2} | Passed: {3}" -f $critical, $errors, $warnings, $passed)
Write-Host ("Report  : {0}" -f $reportPath)

if ($critical -gt 0 -or $errors -gt 0) {
    Write-Host "BUILD GATE BLOCKED." -ForegroundColor Red
    exit 2
}

Write-Host "BUILD GATE PASSED." -ForegroundColor Green
exit 0
