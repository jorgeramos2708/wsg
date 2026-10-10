$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'src'

$violations = @()

# 1. Etiquetas heredadas / claims imprecisos.
$legacy = @('ATTENTION', 'Analysis-first build')
foreach ($token in $legacy) {
    $matches = Get-ChildItem $src -Recurse -File -Include *.xaml,*.cs | Select-String -SimpleMatch $token
    foreach ($match in $matches) {
        $violations += "[legacy-ui] '$token' en $($match.Path):$($match.LineNumber)"
    }
}

# 2. Retención: nunca recursividad genérica.
$retention = Join-Path $src 'WindowsSystemGuardian.Persistence\LocalObservabilityStore.cs'
$retentionText = Get-Content $retention -Raw
if ($retentionText -match 'SearchOption\.AllDirectories') {
    $violations += '[retention] SearchOption.AllDirectories está prohibido.'
}
foreach ($folder in @('evidence','metrics','findings','incidents')) {
    if ($retentionText -notmatch [regex]::Escape('"' + $folder + '"')) {
        $violations += "[retention] Falta carpeta administrada: $folder"
    }
}

# 3. Arquitectura obligatoria de 0.4.1.
$requiredFiles = @(
    'src\WindowsSystemGuardian.Persistence\ReadOnlyObservabilityStore.cs',
    'src\WindowsSystemGuardian.Persistence\ObservabilityDataPathResolver.cs',
    'src\WindowsSystemGuardian.Core\Observability\MetricHistoryBuffer.cs',
    'src\WindowsSystemGuardian.Core\Health\StorageCapacityPolicy.cs',
    'src\WindowsSystemGuardian.Core\Correlation\RootCauseCorrelationEngine.cs',
    'src\WindowsSystemGuardian.Core\Incidents\IncidentEngine.cs',
    'src\WindowsSystemGuardian.Core\Incidents\IncidentLifecycleTracker.cs',
    'src\WindowsSystemGuardian.Core\Recommendations\RecommendationEngine.cs',
    'src\WindowsSystemGuardian.Windows\Observability\WindowsProcessResourceCollector.cs',
    'src\WindowsSystemGuardian.Windows\Observability\WindowsDiskPerformanceCollector.cs',
    'src\WindowsSystemGuardian.Windows\Observability\WindowsNetworkPerformanceCollector.cs',
    'src\WindowsSystemGuardian.UI\Views\IncidentsView.xaml',
    'src\WindowsSystemGuardian.UI\ViewModels\IncidentsViewModel.cs',
    'src\WindowsSystemGuardian.Windows\Diagnostics\ReadOnlyDiagnosticProcessRunner.cs',
    'src\WindowsSystemGuardian.Windows\Diagnostics\WindowsPrivilegeContext.cs',
    'src\WindowsSystemGuardian.Windows\Integrity\WindowsFileSystemHealthCollector.cs',
    'src\WindowsSystemGuardian.Windows\Integrity\WindowsFileSystemScanCollector.cs',
    'src\WindowsSystemGuardian.Windows\Integrity\WindowsSystemFileVerificationCollector.cs',
    'src\WindowsSystemGuardian.Windows\Servicing\WindowsComponentStoreCheckCollector.cs',
    'src\WindowsSystemGuardian.Windows\Servicing\WindowsComponentStoreScanCollector.cs',
    'src\WindowsSystemGuardian.Windows\Servicing\WindowsUpdateHealthCollector.cs',
    'src\WindowsSystemGuardian.Windows\Recovery\WindowsRecoveryHealthCollector.cs',
    'src\WindowsSystemGuardian.Windows\Backup\WindowsVssHealthCollector.cs',
    'src\WindowsSystemGuardian.UI\Views\IntegrityView.xaml',
    'src\WindowsSystemGuardian.UI\ViewModels\IntegrityViewModel.cs'
)
foreach ($relative in $requiredFiles) {
    if (-not (Test-Path (Join-Path $root $relative))) {
        $violations += "[architecture] Falta $relative"
    }
}

# 4. No reintroducir lógica duplicada de capacidad en ViewModels.
$volumeVm = Get-Content (Join-Path $src 'WindowsSystemGuardian.UI\ViewModels\VolumeItemViewModel.cs') -Raw
if ($volumeVm -match 'freePct\s*<' -or $volumeVm -match 'freeGiB\s*<') {
    $violations += '[storage] VolumeItemViewModel volvió a implementar umbrales de capacidad.'
}

# 5. VBS/nested/fail-closed deben estar representados.
$classifier = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Environment\EnvironmentClassifier.cs') -Raw
foreach ($required in @('UsesWindowsHypervisorForSecurity','IsNestedVirtualization','ServiceProbeSucceeded','MAXIMUM_PROTECTION')) {
    if ($classifier -notmatch [regex]::Escape($required)) {
        $violations += "[environment] Falta señal/guard: $required"
    }
}

# 6. Estado inicial no puede presentarse como crítico sin captura.
$observabilityVm = Get-Content (Join-Path $src 'WindowsSystemGuardian.UI\ViewModels\ObservabilityViewModel.cs') -Raw
if ($observabilityVm -notmatch 'Analizando') {
    $violations += '[ui] No existe estado neutral Analizando antes de la primera captura.'
}

# 7. Claims: protección de WSG debe distinguirse de seguridad global de Windows.
$dashboard = Get-Content (Join-Path $src 'WindowsSystemGuardian.UI\Views\DashboardView.xaml') -Raw
if ($dashboard -notmatch 'Modo protegido WSG') {
    $violations += '[ui] La tarjeta de protección no está etiquetada como Modo protegido WSG.'
}
if ($dashboard -notmatch 'Incidentes correlacionados') {
    $violations += '[ui] Dashboard no expone incidentes correlacionados.'
}

# 8. Incident lifecycle debe ser verificable y persistente.
$incidentContract = Get-Content (Join-Path $src 'WindowsSystemGuardian.Contracts\Incidents\Incident.cs') -Raw
foreach ($required in @('ConsecutiveClearChecks','ResolvedAtUtc','VerificationCollectors','StateChangedAtUtc')) {
    if ($incidentContract -notmatch [regex]::Escape($required)) {
        $violations += "[incident] Falta propiedad de ciclo de vida: $required"
    }
}
$lifecycle = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Incidents\IncidentLifecycleTracker.cs') -Raw
if ($lifecycle -notmatch 'clearChecks\s*>=\s*2') {
    $violations += '[incident] La resolución no exige dos comprobaciones limpias.'
}
if ($lifecycle -notmatch 'required\.All\(successfulCollectors\.Contains\)') {
    $violations += '[incident] La resolución no exige cobertura de colectores por dominio.'
}
if ($lifecycle -notmatch 'previous\.State\s*==\s*IncidentState\.Active') {
    $violations += '[incident] StateChangedAtUtc puede reescribirse en cada observación activa.'
}

# 9. La fase 0.4.1 solo diagnostica/recomienda. No debe habilitar reparación automática.
$recommendation = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Recommendations\RecommendationEngine.cs') -Raw
if ($recommendation -match 'RecommendationRisk\.[A-Za-z]+,\s*(?:true|false),\s*(?:true|false),\s*true,\s*"') {
    $violations += '[remediation] Se detectó una recomendación con automatización habilitada.'
}
if ($recommendation -notmatch 'ninguna se ejecuta') {
    $violations += '[remediation] Falta declaración explícita de modo recomendación-only.'
}

# 10. Correlación determinística obligatoria; no shell/IA autónoma.
$rootCause = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Correlation\RootCauseCorrelationEngine.cs') -Raw
foreach ($kind in @('MemoryPressureByProcess','CpuSaturationByProcess','StorageIoReliability','HardwareInstability','HardwareError','ProcessResourceGrowth')) {
    if ($rootCause -notmatch [regex]::Escape($kind)) {
        $violations += "[correlation] Falta regla esperada: $kind"
    }
}

# 11. Hardening causal: no resolver desde UI read-only ni correlacionar por mera coexistencia.
$observabilityService = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Observability\ObservabilityService.cs') -Raw
if ($observabilityService -notmatch 'store\.IsReadOnly') {
    $violations += '[incident] La UI read-only podría avanzar el ciclo de vida autoritativo.'
}
$incidentEngine = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Incidents\IncidentEngine.cs') -Raw
foreach ($guard in @('ProcessCorrelationTolerance','HardwareCorrelationTolerance','AreTemporallyRelated','EntityMatchesScope','EvidenceCategory.Observability')) {
    if ($incidentEngine -notmatch [regex]::Escape($guard)) {
        $violations += "[correlation] Falta guard causal: $guard"
    }
}
if ($incidentEngine -notmatch 'StateChangedAtUtc:\s*null') {
    $violations += '[incident] IncidentEngine debe delegar el timestamp de transición al lifecycle tracker.'
}

# 12. Atribución de recursos debe ser fresca y cubrir handles/hilos además de CPU/RAM.
$findingEngine = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Observability\FindingEngine.cs') -Raw
if ($findingEngine -notmatch 'IsFresh\(systemCpu\[\^1\]\.TimestampUtc') {
    $violations += '[performance] La saturación CPU histórica podría bajar el umbral de procesos actuales.'
}
$processCollector = Get-Content (Join-Path $src 'WindowsSystemGuardian.Windows\Observability\WindowsProcessResourceCollector.cs') -Raw
foreach ($metric in @('process.handles','process.threads','process.memory.private_bytes','process.cpu.percent')) {
    if ($processCollector -notmatch [regex]::Escape($metric)) {
        $violations += "[performance] Falta telemetría por proceso: $metric"
    }
}

# 13. Event Log: backlog debe avanzar de antiguo a nuevo sin saltar el lote pendiente.
$eventCollector = Get-Content (Join-Path $src 'WindowsSystemGuardian.Windows\Observability\WindowsEventLogCollector.cs') -Raw
foreach ($guard in @('EventRecordID >','ReverseDirection = false','lastProcessedRecordId','eventlog-backlog')) {
    if ($eventCollector -notmatch [regex]::Escape($guard)) {
        $violations += "[eventlog] Falta guard de backfill: $guard"
    }
}

# 14. Integridad/servicing 0.4.1: deep scans deben ser High y remediación bloqueada.
$highCollectors = @(
    'WindowsComponentStoreScanCollector.cs',
    'WindowsSystemFileVerificationCollector.cs',
    'WindowsFileSystemScanCollector.cs'
)
foreach ($file in $highCollectors) {
    $path = Get-ChildItem (Join-Path $src 'WindowsSystemGuardian.Windows') -Recurse -File -Filter $file | Select-Object -First 1
    if ($null -eq $path) {
        $violations += "[integrity] Falta collector High: $file"
        continue
    }
    $text = Get-Content $path.FullName -Raw
    if ($text -notmatch 'DiagnosticCost\.High') {
        $violations += "[integrity] El diagnóstico profundo no está marcado High: $file"
    }
}

$runner = Get-Content (Join-Path $src 'WindowsSystemGuardian.Windows\Diagnostics\ReadOnlyDiagnosticProcessRunner.cs') -Raw
foreach ($safe in @('/CheckHealth','/ScanHealth','/verifyonly','/info','list writers','/scan')) {
    if ($runner -notmatch [regex]::Escape($safe)) {
        $violations += "[integrity] Falta comando diagnóstico soportado: $safe"
    }
}
if ($runner -match '"[^"\r\n]*\/RestoreHealth[^"\r\n]*"' -or
    $runner -match '"\/scannow"' -or
    $runner -match '"[^"\r\n]*\/(?:spotfix|offlinescanandfix)[^"\r\n]*"') {
    $violations += '[integrity] El runner contiene un switch de reparación prohibido.'
}

$integrityView = Get-Content (Join-Path $src 'WindowsSystemGuardian.UI\Views\IntegrityView.xaml') -Raw
if ($integrityView -notmatch 'no ejecuta reparaciones' -and $integrityView -notmatch 'No incluidos en 0.4.1') {
    $violations += '[ui] IntegrityView no declara el límite read-only de 0.4.1.'
}

$rootCause04 = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Correlation\RootCauseCorrelationEngine.cs') -Raw
foreach ($kind in @('FileSystemIntegrity','ComponentStoreCorruption','SystemFileIntegrity','WindowsUpdateFailure','PendingServicingReboot','RecoveryEnvironmentUnavailable','VssWriterFailure')) {
    if ($rootCause04 -notmatch [regex]::Escape($kind)) {
        $violations += "[integrity] Falta causa determinística 0.4.1: $kind"
    }
}

$recommendation04 = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Recommendations\RecommendationEngine.cs') -Raw
if ($recommendation04 -match 'RecommendationRisk\.[A-Za-z]+,\s*(?:true|false),\s*(?:true|false),\s*true,\s*"') {
    $violations += '[remediation] Se habilitó una recomendación automática en 0.4.1.'
}

# 15. Compatibilidad de histórico y privilegios.
$categoryEnum = Get-Content (Join-Path $src 'WindowsSystemGuardian.Contracts\Observability\EvidenceCategory.cs') -Raw
foreach ($pair in @(
    @{ Name = 'Observability'; Value = 10 },
    @{ Name = 'Recovery'; Value = 11 },
    @{ Name = 'Backup'; Value = 12 }
)) {
    $pattern = $pair.Name + '\s*=\s*' + $pair.Value
    if ($categoryEnum -notmatch $pattern) {
        $violations += "[compatibility] EvidenceCategory $($pair.Name) cambió su valor persistido esperado ($($pair.Value))."
    }
}

foreach ($relative in @(
    'WindowsSystemGuardian.Windows\Servicing\WindowsComponentStoreCheckCollector.cs',
    'WindowsSystemGuardian.Windows\Servicing\WindowsComponentStoreScanCollector.cs',
    'WindowsSystemGuardian.Windows\Integrity\WindowsSystemFileVerificationCollector.cs',
    'WindowsSystemGuardian.Windows\Integrity\WindowsFileSystemScanCollector.cs',
    'WindowsSystemGuardian.Windows\Backup\WindowsVssHealthCollector.cs'
)) {
    $collectorText = Get-Content (Join-Path $src $relative) -Raw
    if ($collectorText -notmatch 'WindowsPrivilegeContext\.IsAdministrator\(\)') {
        $violations += "[privilege] Falta guard de administrador en $relative"
    }
}

# Deep findings deben poder verificarse por su propio collector sin exigir a la
# vez el quick collector de otra cadencia.
$incident04 = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Incidents\IncidentEngine.cs') -Raw
if ($incident04 -notmatch 'filesystem-scan[\s\S]*?Escaneo profundo del sistema de archivos[\s\S]*?yield break') {
    $violations += '[verification] El filesystem deep scan puede quedar imposible de resolver por collectors incompatibles.'
}
if ($incident04 -notmatch 'component-store-deep[\s\S]*?Escaneo profundo del almacén de componentes[\s\S]*?yield break') {
    $violations += '[verification] El Component Store deep scan puede quedar imposible de resolver por collectors incompatibles.'
}

# 16. Calidad básica del source.
$todoMatches = Get-ChildItem $src -Recurse -File -Include *.cs,*.xaml | Select-String -CaseSensitive -Pattern '\b(?:TODO|FIXME|HACK)\b|NotImplementedException'
foreach ($match in $todoMatches) {
    $violations += "[source] Marcador pendiente en $($match.Path):$($match.LineNumber)"
}


# 17. Guards de regresión de compilación detectados en el primer build real de 0.4.0.
$diagnosticRunner = Get-Content (Join-Path $src 'WindowsSystemGuardian.Windows\Diagnostics\ReadOnlyDiagnosticProcessRunner.cs') -Raw
if ($diagnosticRunner -match '(?<!System\.)Environment\.(?:SystemDirectory|NewLine)') {
    $violations += '[build-regression] Environment no está calificado con System/global::System en el runner y puede colisionar con WindowsSystemGuardian.Windows.Environment.'
}

$observabilityService04 = Get-Content (Join-Path $src 'WindowsSystemGuardian.Core\Observability\ObservabilityService.cs') -Raw
if ($observabilityService04 -notmatch 'CaptureAsync\(CancellationToken\s+cancellationToken\)') {
    $violations += '[build-regression] Falta overload CaptureAsync(CancellationToken) para compatibilidad de fuente con 0.3.x/tests.'
}

$testRoot = Join-Path $root 'tests'
if (Test-Path $testRoot) {
    $singleWhere = Get-ChildItem $testRoot -Recurse -File -Filter *.cs | Select-String -Pattern 'Assert\.Single\([^\r\n]*\.Where\('
    foreach ($match in $singleWhere) {
        $violations += "[test-quality] Patrón xUnit2031 en $($match.Path):$($match.LineNumber)"
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("QA static audit failed:`n" + ($violations -join "`n"))
}

Write-Host 'QA static audit: PASS'
Write-Host ' - retention recursion blocked and incident store managed'
Write-Host ' - authoritative Agent store architecture present'
Write-Host ' - storage thresholds centralized'
Write-Host ' - VBS/nested/fail-closed environment guards present'
Write-Host ' - incident/root-cause/recommendation architecture present'
Write-Host ' - incident lifecycle requires domain-specific verification before resolution'
Write-Host ' - temporal/entity causal guards and read-only authority guards present'
Write-Host ' - Event Log backlog advances without skipping pending batches'
Write-Host ' - integrity/servicing diagnostics present; deep scans remain High/manual'
Write-Host ' - remediation flows through explicit approval gates and verification'
Write-Host ' - initial UI state neutral and protection claim scoped to WSG'

