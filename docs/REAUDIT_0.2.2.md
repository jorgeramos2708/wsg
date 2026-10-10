# Windows System Guardian 0.2.2 — QA Finalization

## Motivo de la versión

0.2.2 corrige dos defectos de calidad detectados durante el gate runtime de 0.2.1:

1. `qa-static-audit.ps1` producía un falso positivo porque buscaba `TODO` sin distinguir mayúsculas/minúsculas y sin exigir palabra completa. Texto legítimo como `todos los volúmenes` / `no determinados` podía disparar el control.
2. La suite xUnit generaba 14 advertencias `xUnit1051` por no propagar `TestContext.Current.CancellationToken` en operaciones async cancelables.

## Correcciones

- Auditor QA ahora usa búsqueda **case-sensitive** y marcadores con límite de palabra: `TODO`, `FIXME`, `HACK`, `NotImplementedException`.
- Se mantienen detectables los marcadores reales, pero texto normal en español ya no genera falsos positivos.
- Las 14 llamadas señaladas por xUnit ahora propagan `TestContext.Current.CancellationToken`.
- Versionado central actualizado a 0.2.2.
- No se han habilitado operaciones destructivas ni reparación automática.

## Gate heredado de 0.2.1

Validado por ejecución del usuario en Windows antes de este parche:

- Build Release: PASS, 0 errores.
- Unit tests: 37/37 PASS, 0 fallos, 0 omitidos.
- XAML binding audit: PASS.
- Static safety audit: PASS.
- QA static audit: bloqueado únicamente por el falso positivo corregido en esta versión.

## Gate requerido para 0.2.2

Ejecutar:

```powershell
dotnet clean .\WindowsSystemGuardian.sln
dotnet restore .\WindowsSystemGuardian.sln
dotnet build .\WindowsSystemGuardian.sln -c Release
dotnet test .\WindowsSystemGuardian.sln -c Release
.\tools\xaml-binding-audit.ps1
.\tools\static-safety-audit.ps1
.\tools\qa-static-audit.ps1
```

Objetivo: 0 errores, 0 warnings controlables, 37/37 tests y las tres auditorías en PASS.
