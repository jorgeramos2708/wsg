using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>
/// Parseo determinista de la salida de DISM /RestoreHealth y SFC /scannow hacia un
/// veredicto. Fail-closed: timeout, exit code distinto de cero, salida vacía o texto
/// no reconocido producen Indeterminate; nunca se interpreta éxito sin evidencia.
/// DISM se ejecuta con /English; SFC se parsea en inglés y español (su salida sigue
/// el idioma del sistema operativo).
/// </summary>
public static class RemediationOutcomeParser
{
    public static RemediationParsedVerdict Parse(
        RemediationActionId action,
        int exitCode,
        bool timedOut,
        string? output)
    {
        if (action is not (RemediationActionId.ComponentStoreRepair
            or RemediationActionId.SystemFileRepair
            or RemediationActionId.WindowsUpdateServiceRestart))
            return RemediationParsedVerdict.Indeterminate;

        if (timedOut)
            return RemediationParsedVerdict.Indeterminate;

        var text = output ?? string.Empty;

        if (ContainsAny(text, "operation was cancelled", "se canceló la operación", "operación cancelada"))
            return RemediationParsedVerdict.Cancelled;

        if (exitCode != 0)
            return RemediationParsedVerdict.Indeterminate;

        if (string.IsNullOrWhiteSpace(text))
            return RemediationParsedVerdict.Indeterminate;

        return action switch
        {
            RemediationActionId.ComponentStoreRepair => ParseComponentStore(text),
            RemediationActionId.WindowsUpdateServiceRestart => ParseServiceRestart(text),
            _ => ParseSystemFiles(text)
        };
    }

    private static RemediationParsedVerdict ParseServiceRestart(string text)
    {
        // PrivilegedHost emite el marcador determinista WU_RESTART_OK solo si todos
        // los pasos (stop bits/wuauserv, start wuauserv/bits) terminaron en Running/Stopped.
        return ContainsAny(text, "WU_RESTART_OK")
            ? RemediationParsedVerdict.Repaired
            : RemediationParsedVerdict.Indeterminate;
    }

    private static RemediationParsedVerdict ParseComponentStore(string text)
    {
        if (ContainsAny(text,
                "restore operation completed successfully",
                "la operación de restauración se completó correctamente"))
            return RemediationParsedVerdict.Repaired;

        if (ContainsAny(text,
                "no component store corruption detected",
                "no se detectó ninguna corrupción"))
            return RemediationParsedVerdict.NoDamageFound;

        return RemediationParsedVerdict.Indeterminate;
    }

    private static RemediationParsedVerdict ParseSystemFiles(string text)
    {
        if (ContainsAny(text,
                "found corrupt files and successfully repaired them",
                "encontró archivos dañados y los reparó"))
            return RemediationParsedVerdict.Repaired;

        if (ContainsAny(text,
                "did not find any integrity violations",
                "no encontró ninguna violación de integridad"))
            return RemediationParsedVerdict.NoDamageFound;

        if (ContainsAny(text,
                "unable to fix some of them",
                "no tuvo la capacidad de corregir"))
            // Reparación parcial: no se afirma éxito ni no-reparabilidad.
            return RemediationParsedVerdict.Indeterminate;

        return RemediationParsedVerdict.Indeterminate;
    }

    private static bool ContainsAny(string text, params string[] patterns) =>
        patterns.Any(pattern =>
            text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
}
