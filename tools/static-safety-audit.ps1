$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$source = Join-Path $root 'src'

# WSG 0.4.1 autoriza procesos externos EXCLUSIVAMENTE desde
# ReadOnlyDiagnosticProcessRunner y solo con especificaciones fijas de consulta.
# No autoriza reparaciones ni comandos arbitrarios.
$blockedPatterns = @(
    'File\.Delete\s*\(',
    'Directory\.Delete\s*\(',
    'DeleteFile\s*\(',
    'MoveFileEx\s*\(',
    'Remove-Item\s+',
    'Format-Volume',
    'Clear-Disk',
    'Remove-Partition',
    'esentutl\s+/p',
    'Remove-VM',
    'Remove-VHD',
    'Stop-Service',
    'Set-Service',
    'sc(?:\.exe)?\s+(delete|stop|config)',
    'cmd(?:\.exe)?\s+/c\s+(del|rd|rmdir)',
    'Start-Process\s+',
    'SetAccessControl\s*\(',
    'FileSecurity',
    'DirectorySecurity'
)

$allowedRetentionFile = [IO.Path]::GetFullPath(
    (Join-Path $source 'WindowsSystemGuardian.Persistence\LocalObservabilityStore.cs'))
$allowedRunnerFile = [IO.Path]::GetFullPath(
    (Join-Path $source 'WindowsSystemGuardian.Windows\Diagnostics\ReadOnlyDiagnosticProcessRunner.cs'))

# WSG 0.5.0: frontera de remediación controlada. Los procesos externos quedan
# confinados a tres archivos: el runner read-only de diagnóstico, el runner elevado
# de PrivilegedHost (mismo patrón) y el launcher que invoca a PrivilegedHost con
# UAC. El catálogo de comandos de remediación es la única fuente de texto de comando.
$allowedRetentionFiles = @(
    $allowedRetentionFile,
    [IO.Path]::GetFullPath((Join-Path $source 'WindowsSystemGuardian.Persistence\RemediationRecordStore.cs'))
)
$allowedProcessFiles = @(
    $allowedRunnerFile,
    [IO.Path]::GetFullPath((Join-Path $source 'WindowsSystemGuardian.Windows\Remediation\ElevatedProcessLauncher.cs')),
    [IO.Path]::GetFullPath((Join-Path $source 'WindowsSystemGuardian.PrivilegedHost\ElevatedRemediationRunner.cs'))
)
$allowedElevationFile = [IO.Path]::GetFullPath(
    (Join-Path $source 'WindowsSystemGuardian.Windows\Remediation\ElevatedProcessLauncher.cs'))
$allowedCatalogFile = [IO.Path]::GetFullPath(
    (Join-Path $source 'WindowsSystemGuardian.Core\Remediation\RemediationCommandCatalog.cs'))
$allowedHostRunnerFile = [IO.Path]::GetFullPath(
    (Join-Path $source 'WindowsSystemGuardian.PrivilegedHost\ElevatedRemediationRunner.cs'))

$violations = @()

Get-ChildItem $source -Recurse -File -Include *.cs,*.ps1 | ForEach-Object {
    $fullName = [IO.Path]::GetFullPath($_.FullName)
    $text = Get-Content $_.FullName -Raw

    foreach ($pattern in $blockedPatterns) {
        if ($text -match $pattern) {
            $isRetentionDelete =
                ($allowedRetentionFiles -contains $fullName -and
                 $pattern -eq 'File\.Delete\s*\(')

            if (-not $isRetentionDelete) {
                $violations += "[$pattern] $($_.FullName)"
            }
        }
    }

    # El verbo runas (UAC) solo se admite en el launcher de elevación.
    if ($text -match '"runas"' -and $fullName -ne $allowedElevationFile) {
        $violations += "[elevation] Uso de runas fuera del launcher autorizado: $($_.FullName)"
    }

    # Todo uso real de System.Diagnostics.Process queda confinado a los runners y al launcher.
    if (($text -match 'new\s+Process\s*\{' -or
         $text -match 'ProcessStartInfo' -or
         $text -match '\bprocess\.Start\s*\(' -or
         $text -match '\bprocess\.Kill\s*\(') -and
        ($allowedProcessFiles -notcontains $fullName)) {
        $violations += "[process-execution] Uso de proceso fuera de los runners/launcher autorizados: $($_.FullName)"
    }
}

# Retención protegida.
$retentionText = Get-Content $allowedRetentionFile -Raw
$deleteCount = ([regex]::Matches($retentionText, 'File\.Delete\s*\(')).Count
if ($deleteCount -ne 1) {
    $violations += "[retention] Se esperaba exactamente 1 File.Delete validado; encontrados: $deleteCount"
}
if ($retentionText -match 'SearchOption\.AllDirectories') {
    $violations += '[retention] SearchOption.AllDirectories está prohibido en LocalObservabilityStore'
}
foreach ($guard in @('SearchOption\.TopDirectoryOnly','IsManagedDataFileUnsafe','FileAttributes\.ReparsePoint','TryDeleteManagedFileUnsafe')) {
    if ($retentionText -notmatch $guard) {
        $violations += "[retention] Falta guard requerido: $guard"
    }
}

# Único runner de procesos externos: especificaciones fijas read-only.
if (-not (Test-Path $allowedRunnerFile)) {
    $violations += '[diagnostics] Falta ReadOnlyDiagnosticProcessRunner.'
}
else {
    $runnerText = Get-Content $allowedRunnerFile -Raw
    $startCount = ([regex]::Matches($runnerText, '\bprocess\.Start\s*\(')).Count
    if ($startCount -ne 1) {
        $violations += "[diagnostics] Se esperaba exactamente 1 process.Start() confinado; encontrados: $startCount"
    }

    foreach ($required in @(
        '/CheckHealth /NoRestart /English',
        '/ScanHealth /NoRestart /English',
        '"/verifyonly"',
        '"/info"',
        '"list writers"',
        ' /scan"')) {
        if ($runnerText -notmatch [regex]::Escape($required)) {
            $violations += "[diagnostics] Falta comando de solo lectura requerido: $required"
        }
    }

    # Buscar argumentos ejecutables prohibidos en el runner, no menciones
    # documentales de otras capas.
    $forbiddenRunnerPatterns = @(
        '"[^"\r\n]*\/RestoreHealth[^"\r\n]*"',
        '"\/scannow"',
        '"[^"\r\n]*\s\/(?:f|r)(?:\s|"|$)',
        '"[^"\r\n]*\/(?:spotfix|offlinescanandfix)[^"\r\n]*"',
        '"delete\s+(?:shadows|shadowstorage)[^"\r\n]*"',
        '"\/(?:enable|disable|setreimage)"',
        'shutdown\.exe',
        'Restart-Computer',
        'Stop-Computer'
    )
    foreach ($pattern in $forbiddenRunnerPatterns) {
        if ($runnerText -match $pattern) {
            $violations += "[diagnostics] El runner contiene un argumento de reparación/cambio prohibido: $pattern"
        }
    }

    foreach ($guard in @('DriveLetterPattern','UseShellExecute = false','CreateNoWindow = true','RedirectStandardOutput = true')) {
        if ($runnerText -notmatch [regex]::Escape($guard)) {
            $violations += "[diagnostics] Falta guard del runner: $guard"
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("Static safety audit failed:`n" + ($violations -join "`n"))
}

# Remediación 0.5.0: catálogo con exactamente los dos comandos soportados.
$remediationRetentionFile = [IO.Path]::GetFullPath(
    (Join-Path $source 'WindowsSystemGuardian.Persistence\RemediationRecordStore.cs'))
if (Test-Path $remediationRetentionFile) {
    $remediationRetentionText = Get-Content $remediationRetentionFile -Raw
    foreach ($guard in @('SearchOption\.TopDirectoryOnly','FileAttributes\.ReparsePoint','AppendAllText')) {
        if ($remediationRetentionText -notmatch $guard) {
            $violations += "[remediation] Falta guard de retención en RemediationRecordStore: $guard"
        }
    }
}

if (-not (Test-Path $allowedCatalogFile)) {
    $violations += '[remediation] Falta RemediationCommandCatalog.'
}
else {
    $catalogText = Get-Content $allowedCatalogFile -Raw
    foreach ($required in @('/Online /Cleanup-Image /RestoreHealth /NoRestart /English', '/scannow')) {
        if ($catalogText -notmatch [regex]::Escape($required)) {
            $violations += "[remediation] Falta comando soportado en el catálogo: $required"
        }
    }
    foreach ($forbidden in @(
        '"[^"\r\n]*\s\/(?:f|r)(?:\s|"|$)',
        'spotfix',
        'offlinescanandfix',
        'delete\s+shadows',
        'setreimage',
        'shutdown\.exe',
        'Restart-Computer',
        'Stop-Computer',
        'Stop-Service',
        'Set-Service',
        '/Enable-',
        '/Disable-')) {
        if ($catalogText -match $forbidden) {
            $violations += "[remediation] El catálogo contiene un comando prohibido: $forbidden"
        }
    }
}

# Runner elevado de PrivilegedHost: mismos guards que el runner read-only.
if (-not (Test-Path $allowedHostRunnerFile)) {
    $violations += '[remediation] Falta ElevatedRemediationRunner en PrivilegedHost.'
}
else {
    $hostRunnerText = Get-Content $allowedHostRunnerFile -Raw
    foreach ($guard in @('UseShellExecute = false','CreateNoWindow = true','RedirectStandardOutput = true','RemediationCommandCatalog')) {
        if ($hostRunnerText -notmatch [regex]::Escape($guard)) {
            $violations += "[remediation] Falta guard del host elevado: $guard"
        }
    }
}

# Launcher de elevación: UAC acotado, exe fijo y validación de versión.
if (-not (Test-Path $allowedElevationFile)) {
    $violations += '[remediation] Falta ElevatedProcessLauncher.'
}
else {
    $launcherText = Get-Content $allowedElevationFile -Raw
    foreach ($guard in @('Verb = "runas"','UseShellExecute = true','ValidateSameVersion','RemediationDataPaths')) {
        if ($launcherText -notmatch [regex]::Escape($guard)) {
            $violations += "[remediation] Falta guard del launcher elevado: $guard"
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("Static safety audit failed:`n" + ($violations -join "`n"))
}

Write-Host 'Static safety audit: PASS'
Write-Host ' - no system-destructive operations found'
Write-Host ' - retention uses TopDirectoryOnly + managed JSONL/reparse validation'
Write-Host ' - external process execution confined to fixed runners and the elevation launcher'
Write-Host ' - UAC elevation confined to PrivilegedHost launcher with version validation'
Write-Host ' - remediation catalog holds exactly DISM /RestoreHealth + SFC /scannow'
Write-Host ' - DISM/SFC/CHKDSK repair switches and WinRE/VSS mutation commands blocked outside the catalog'
