$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ui = Join-Path $root 'src\WindowsSystemGuardian.UI'
$violations = @()

Get-ChildItem $ui -Recurse -File -Filter *.xaml | ForEach-Object {
    $lines = Get-Content $_.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $matches = [regex]::Matches($line, '\{Binding\s+[^\}]+\}')
        foreach ($match in $matches) {
            # Las vistas de 0.4.1 son de lectura/observabilidad y diagnóstico. Requerimos OneWay
            # explícito para que ninguna DependencyProperty intente escribir en
            # propiedades calculadas de los ViewModels.
            if ($match.Value -notmatch 'Mode\s*=\s*OneWay') {
                $violations += "$($_.FullName):$($i + 1): Binding must explicitly use Mode=OneWay: $($match.Value)"
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("XAML binding audit failed:`n" + ($violations -join "`n"))
}

Write-Host 'XAML binding audit: PASS (all current UI bindings are explicit OneWay)'
