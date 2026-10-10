> Documento histórico de la fase 0.2.0. La reauditoría vigente se encuentra en `REAUDIT_0.2.2.md`.

# Auditoría estática histórica — Windows System Guardian 0.2.0

## Resultado

- Proyectos en solución: 7.
- XAML: 9 archivos.
- Bindings revisados: 79.
- Bindings sin `Mode=OneWay`: 0.
- Errores de parseo XML/XAML: 0.
- Errores de balance léxico C# detectados por auditoría auxiliar: 0.
- Strings UI heredados `ATTENTION`, `PROTECTED`, `Analysis-first build`: 0.
- Operaciones destructivas de sistema encontradas: 0.
- `File.Delete`: 1 uso deliberado, confinado a `LocalObservabilityStore` para retención de archivos JSONL propios de WSG.

## Límites de validación

El entorno de generación no dispone de .NET SDK/Windows WPF, por lo que la validación final de compilación y runtime debe ejecutarse en Windows con:

```powershell
dotnet clean .\WindowsSystemGuardian.sln
dotnet restore .\WindowsSystemGuardian.sln
dotnet build .\WindowsSystemGuardian.sln -c Release
dotnet test .\WindowsSystemGuardian.sln -c Release
.\tools\xaml-binding-audit.ps1
.\tools\static-safety-audit.ps1
```

## Riesgos deliberadamente no habilitados

- no existe CleanupExecutor destructivo;
- no existe RepairPlanner ejecutor;
- no se detienen/inician servicios automáticamente;
- no se ejecuta DISM/SFC/CHKDSK desde WSG;
- no se modifica AD, Hyper-V, cluster, BCD, Windows Update ni seguridad;
- los Findings son recomendaciones/diagnóstico, no permisos de modificación.
