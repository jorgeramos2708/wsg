namespace WindowsSystemGuardian.Core.Diagnostics;

/// <summary>Clasificación de los estados de VSS writers reportados por vssadmin list writers.</summary>
public sealed record VssWriterClassification(
    int Total,
    int Stable,
    int Transient,
    int Failed);

/// <summary>
/// Clasifica los códigos de estado de VSS writers (el campo <c>[n]</c> de la salida de
/// <c>vssadmin list writers</c>, numérico e independiente del idioma). Los estados
/// transitorios (2-8: Waiting for subscribe/snapshot/post-snapshot/backup-complete/
/// completion) son normales durante actividad de respaldo y NO son fallos: demostrado
/// empíricamente con estado [5] "Esperando finalización" en un sistema sano. Solo el
/// estado 0 (undefined) y los estados Failed* (≥9) se tratan como fallo. El enum
/// VSS_WRITER_STATE (vswriter.h) no tenía página en Learn al momento de la auditoría
/// (docs/AUDIT_MSDOC_0.5.0.md); clasificación respaldada por evidencia empírica y SDK.
/// </summary>
public static class VssWriterStateInterpreter
{
    public static VssWriterClassification Classify(IReadOnlyList<int> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        return new VssWriterClassification(
            Total: states.Count,
            Stable: states.Count(s => s == 1),
            Transient: states.Count(s => s is >= 2 and <= 8),
            Failed: states.Count(s => s == 0 || s >= 9));
    }
}
