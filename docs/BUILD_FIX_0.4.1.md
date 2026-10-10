# Windows System Guardian 0.4.1 — Reauditoría Build/QA Hotfix

## Motivo

El primer build real de 0.4.0 en Windows reportó **8 errores y 13 advertencias**. La revisión 0.4.1 corrige exclusivamente esos problemas de compilación/QA y conserva el alcance funcional de Windows Integrity & Servicing Health.

## Errores corregidos

### 1. CS0234 — colisión de `Environment`

Archivo:

`src/WindowsSystemGuardian.Windows/Diagnostics/ReadOnlyDiagnosticProcessRunner.cs`

El namespace `WindowsSystemGuardian.Windows.Environment` sombreaba a `System.Environment`.

Se corrigieron las cuatro referencias a:

- `global::System.Environment.SystemDirectory`
- `global::System.Environment.NewLine`

Esto evita que el compilador intente resolver `SystemDirectory` o `NewLine` dentro de `WindowsSystemGuardian.Windows.Environment`.

### 2. CS1503 — cambio incompatible de `CaptureAsync`

Archivo:

`src/WindowsSystemGuardian.Core/Observability/ObservabilityService.cs`

0.4.0 añadió `DiagnosticCost` como primer parámetro de `CaptureAsync`, rompiendo llamadas existentes que pasaban un `CancellationToken` posicionalmente.

0.4.1 conserva la API nueva y restaura compatibilidad de fuente con:

```csharp
public Task<ObservabilitySnapshot> CaptureAsync(CancellationToken cancellationToken) =>
    CaptureAsync(DiagnosticCost.Medium, cancellationToken);
```

Así siguen siendo válidas tanto:

```csharp
CaptureAsync(DiagnosticCost.High, token)
```

como:

```csharp
CaptureAsync(token)
```

## Advertencias corregidas

### 3. CS8604 — 8 advertencias nullable

Los tests de `IntegrityIncidentTests` ahora manejan explícitamente `VerificationCollectors` como colección opcional mediante `?? []`.

### 4. xUnit2031 — 5 advertencias

Se reemplazó:

```csharp
Assert.Single(collection.Where(predicate))
```

por:

```csharp
Assert.Single(collection, predicate)
```

según el analyzer de xUnit.

## Prevención de regresión

`tools/qa-static-audit.ps1` ahora comprueba también:

- que `Environment.SystemDirectory/NewLine` no queden sin calificar en el runner;
- que exista `CaptureAsync(CancellationToken)`;
- que no reaparezca el patrón `Assert.Single(...Where(...))` en tests.

## Reauditoría estática del árbol 0.4.1

```text
C# total:                    120
C# productivo:               110
XAML:                         11
Bindings:                    131
Bindings sin OneWay:           0

[Fact]:                       81
[Theory]:                      1
[InlineData]:                  8
Casos runtime esperados:      89

Errores XML/XAML:              0
Marcadores TODO/FIXME/HACK:    0
NotImplementedException:       0
SearchOption.AllDirectories:   0
Regresión Environment:         0
Regresión CaptureAsync:        0
Patrones xUnit2031:            0
Problemas estáticos:           0
```

## Seguridad

0.4.1 no habilita reparaciones. Se conserva:

- `DISM /CheckHealth` y `/ScanHealth`, sin `/RestoreHealth`;
- `SFC /verifyonly`, sin `/scannow`;
- `CHKDSK /scan`, sin `/f`, `/r`, `/spotfix`;
- `REAgentC /info`, sin cambios de WinRE;
- VSS en consulta;
- una única llamada productiva a `File.Delete`, confinada a retención segura de JSONL propios de WSG.

## Veredicto

**GO ESTÁTICO / BUILD-RUNTIME PENDIENTE EN WINDOWS**

El siguiente gate debe ejecutarse en Windows y debe alcanzar:

- 0 errores de compilación;
- 0 advertencias controlables;
- 89/89 pruebas;
- `xaml-binding-audit.ps1` = PASS;
- `static-safety-audit.ps1` = PASS;
- `qa-static-audit.ps1` = PASS.
