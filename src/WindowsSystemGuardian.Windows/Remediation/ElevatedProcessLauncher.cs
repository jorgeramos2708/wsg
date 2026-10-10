using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Abstractions;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Windows.Remediation;

/// <summary>
/// ÚNICO punto de WSG que lanza un proceso con elevación (Verb="runas"). Excepción
/// deliberada y acotada a la regla UseShellExecute=false de ReadOnlyDiagnosticProcessRunner:
/// el UAC del shell es el mecanismo documentado para obtener un host de ejecución
/// separado y elevado. Límites estrictos: el ejecutable es PrivilegedHost.exe del
/// propio directorio de la aplicación (misma versión), los argumentos salen de la
/// gramática de ElevatedHostLaunchSpec (enum + guid + ruta de resultado), y el
/// resultado JSON se valida contra el plan antes de devolverse. No se aceptan
/// ejecutables, argumentos ni rutas arbitrarias.
/// </summary>
public sealed class ElevatedProcessLauncher : IElevatedRemediationLauncher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Margen adicional sobre el timeout del plan para esperar el JSON de resultado.</summary>
    private static readonly TimeSpan ResultWaitBuffer = TimeSpan.FromMinutes(2);

    public async Task<RemediationOutcome> LaunchAsync(
        RemediationPlan plan,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var hostPath = ResolveHostExecutable();
        var resultsDirectory = RemediationDataPaths.EnsureResultsDirectory();
        var outputPath = Path.Combine(resultsDirectory, $"outcome-{requestId:N}.json");
        var arguments = ElevatedHostLaunchSpec.BuildArguments(plan.Action, requestId, outputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = arguments,
            // Justificación de la excepción: UseShellExecute=true es necesario para el
            // verbo "runas" (UAC). El FileName está fijado arriba; nunca proviene de datos externos.
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows no pudo iniciar PrivilegedHost.");

        var overallTimeout = plan.Timeout + ResultWaitBuffer;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(overallTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException("PrivilegedHost no terminó dentro de la ventana de tiempo permitida.");
        }

        if (!File.Exists(outputPath))
            throw new InvalidOperationException(
                $"PrivilegedHost terminó (exit={process.ExitCode}) sin escribir el resultado estructurado.");

        var json = await File.ReadAllTextAsync(outputPath, cancellationToken);
        var outcome = JsonSerializer.Deserialize<RemediationOutcome>(json, JsonOptions)
            ?? throw new InvalidOperationException("El resultado de PrivilegedHost no se pudo interpretar.");

        // Validación fail-closed del contrato antes de aceptar el resultado.
        if (outcome.SchemaVersion != RemediationOutcome.CurrentSchemaVersion)
            throw new InvalidOperationException($"Versión de contrato inesperada: {outcome.SchemaVersion}.");
        if (outcome.Action != plan.Action || outcome.RequestId != requestId)
            throw new InvalidOperationException("El resultado no corresponde a la ejecución solicitada.");

        return outcome;
    }

    private static string ResolveHostExecutable()
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, "WindowsSystemGuardian.PrivilegedHost.exe");
        if (!File.Exists(hostPath))
            throw new InvalidOperationException(
                "PrivilegedHost.exe no está desplegado junto a la aplicación; no se ejecuta ninguna remediación.");

        ValidateSameVersion(hostPath);
        return hostPath;
    }

    private static void ValidateSameVersion(string hostPath)
    {
        var expected = typeof(ElevatedProcessLauncher).Assembly.GetName().Version;
        var hostInfo = FileVersionInfo.GetVersionInfo(hostPath);
        var hostVersion = hostInfo.FileVersion is null ? null : new Version(hostInfo.FileVersion);

        if (expected is null || hostVersion is null ||
            hostVersion.Major != expected.Major ||
            hostVersion.Minor != expected.Minor ||
            hostVersion.Build != expected.Build)
            throw new InvalidOperationException(
                $"La versión de PrivilegedHost ({hostInfo.FileVersion ?? "desconocida"}) no coincide con la de la aplicación ({expected}). " +
                "No se ejecuta ninguna remediación.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Mejor esfuerzo: la cancelación ya se propaga al llamador.
        }
    }
}
