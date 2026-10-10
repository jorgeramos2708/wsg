using WindowsSystemGuardian.Contracts.Remediation;

namespace WindowsSystemGuardian.Core.Remediation;

/// <summary>Invocación elevada parseada y validada de PrivilegedHost.</summary>
public sealed record ElevatedHostInvocation(
    RemediationActionId Action,
    Guid RequestId,
    string OutputFilePath);

/// <summary>
/// Gramática y parser de la línea de comandos de PrivilegedHost. Única fuente para
/// quien lanza (capa Windows) y quien ejecuta (PrivilegedHost). Fail-closed: se
/// rechazan argumentos desconocidos, duplicados, incompletos, acciones fuera del
/// enum soportado (incluido None y valores numéricos) y rutas de salida relativas.
/// </summary>
public static class ElevatedHostLaunchSpec
{
    public static string BuildArguments(RemediationActionId action, Guid requestId, string outputPath)
    {
        Validate(action, requestId, outputPath);
        return FormattableString.Invariant(
            $"--command {action} --request {requestId:D} --out \"{outputPath}\"");
    }

    public static bool TryParse(IReadOnlyList<string> args, out ElevatedHostInvocation invocation, out string error)
    {
        invocation = null!;
        error = string.Empty;

        if (args.Count != 6)
        {
            error = "Se esperaban exactamente los argumentos --command, --request y --out con sus valores.";
            return false;
        }

        string? actionToken = null;
        string? requestToken = null;
        string? outToken = null;

        for (var i = 0; i < args.Count; i += 2)
        {
            var flag = args[i];
            var value = args[i + 1];
            switch (flag.ToLowerInvariant())
            {
                case "--command" when actionToken is null:
                    actionToken = value;
                    break;
                case "--request" when requestToken is null:
                    requestToken = value;
                    break;
                case "--out" when outToken is null:
                    outToken = value;
                    break;
                default:
                    error = $"Argumento no reconocido o duplicado: '{flag}'.";
                    return false;
            }
        }

        if (actionToken is null || requestToken is null || outToken is null)
        {
            error = "Faltan argumentos obligatorios (--command, --request, --out).";
            return false;
        }

        // Fail-closed: se acepta únicamente el nombre exacto del enum soportado. Esto
        // rechaza None, valores numéricos y cualquier nombre fuera del contrato.
        if (!Enum.TryParse<RemediationActionId>(actionToken, ignoreCase: false, out var action) ||
            action is RemediationActionId.None ||
            !string.Equals(Enum.GetName(typeof(RemediationActionId), action), actionToken, StringComparison.Ordinal))
        {
            error = $"Acción de remediación no soportada: '{actionToken}'.";
            return false;
        }

        if (!Guid.TryParseExact(requestToken, "D", out var requestId))
        {
            error = "El identificador de solicitud no es un GUID válido.";
            return false;
        }

        if (!Path.IsPathRooted(outToken))
        {
            error = "La ruta de salida debe ser absoluta.";
            return false;
        }

        invocation = new ElevatedHostInvocation(action, requestId, outToken);
        return true;
    }

    private static void Validate(RemediationActionId action, Guid requestId, string outputPath)
    {
        if (action is RemediationActionId.None ||
            !string.Equals(Enum.GetName(typeof(RemediationActionId), action), action.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("Acción de remediación no soportada.");

        if (requestId == Guid.Empty)
            throw new InvalidOperationException("Se requiere un identificador de solicitud no vacío.");

        if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathRooted(outputPath))
            throw new InvalidOperationException("La ruta de salida del resultado debe ser absoluta.");
    }
}
