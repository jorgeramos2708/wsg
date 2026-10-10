using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.PrivilegedHost;

internal sealed record ElevatedRunnerResult(
    int ExitCode,
    bool TimedOut,
    string Output);

/// <summary>
/// Ejecuta exclusivamente las operaciones del catálogo de remediación: ProcessCommand
/// (DISM /RestoreHealth, SFC /scannow resueltos desde System32, con timeout, árbol de
/// procesos bajo control y salida acotada) y ServiceRestart (reinicio controlado de
/// bits y wuauserv mediante la API oficial System.ServiceProcess.ServiceController —
/// learn.microsoft.com/dotnet/api/system.serviceprocess.servicecontroller). Mismo
/// patrón que ReadOnlyDiagnosticProcessRunner de 0.4.1; no ejecuta comandos arbitrarios.
/// </summary>
internal sealed class ElevatedRemediationRunner
{
    private const int MaximumOutputCharacters = 8_192;

    /// <summary>Timeout por servicio aprobado en la spec 0.6.0 (4 pasos ≤ 5 min del catálogo).</summary>
    private static readonly TimeSpan PerServiceTimeout = TimeSpan.FromSeconds(60);

    public async Task<ElevatedRunnerResult> RunAsync(RemediationActionId action)
    {
        var spec = RemediationCommandCatalog.For(action);
        return spec.Kind switch
        {
            RemediationCommandKind.ServiceRestart => await Task.Run(() => RunServiceRestart(spec)),
            _ => await RunProcessAsync(spec)
        };
    }

    /// <summary>
    /// Reinicio controlado aprobado en la spec 0.6.0: stop(bits) → stop(wuauserv) →
    /// start(wuauserv) → start(bits). Sin borrado de cachés ni servicios distintos.
    /// Marcador determinista WU_RESTART_OK solo si todos los pasos terminaron bien.
    /// </summary>
    private static ElevatedRunnerResult RunServiceRestart(RemediationCommandCatalog.CommandSpec spec)
    {
        var log = new StringBuilder();
        log.AppendLine($"Operación: {spec.DisplayCommand}");

        try
        {
            StopService("bits", log);
            StopService("wuauserv", log);
            StartService("wuauserv", log);
            StartService("bits", log);
            log.AppendLine("WU_RESTART_OK");
            return new ElevatedRunnerResult(0, false, Limit(log.ToString()));
        }
        catch (Exception ex)
        {
            log.AppendLine($"[fallo] {ex.Message}");
            // Restauración best-effort: el estado final no puede dejar WU muerto.
            TryEnsureRunning("wuauserv", log);
            TryEnsureRunning("bits", log);
            return new ElevatedRunnerResult(4, false, Limit(log.ToString()));
        }
    }

    private static void StopService(string serviceName, StringBuilder log)
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status == ServiceControllerStatus.Stopped)
        {
            log.AppendLine($"[stop] {serviceName}: ya estaba detenido.");
            return;
        }

        if (controller.Status != ServiceControllerStatus.StopPending)
            controller.Stop();

        controller.WaitForStatus(ServiceControllerStatus.Stopped, PerServiceTimeout);
        log.AppendLine($"[stop] {serviceName}: detenido.");
    }

    private static void StartService(string serviceName, StringBuilder log)
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status == ServiceControllerStatus.Running)
        {
            log.AppendLine($"[start] {serviceName}: ya estaba en ejecución.");
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, PerServiceTimeout);
        log.AppendLine($"[start] {serviceName}: en ejecución.");
    }

    private static void TryEnsureRunning(string serviceName, StringBuilder log)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status != ServiceControllerStatus.Running)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, PerServiceTimeout);
                log.AppendLine($"[restauración] {serviceName}: en ejecución.");
            }
        }
        catch (Exception restoreError)
        {
            log.AppendLine($"[restauración] {serviceName}: no se pudo asegurar el arranque ({restoreError.Message}).");
        }
    }

    private async Task<ElevatedRunnerResult> RunProcessAsync(RemediationCommandCatalog.CommandSpec spec)
    {
        var executable = Path.Combine(Environment.SystemDirectory, spec.ExecutableName);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = spec.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.SystemDirectory
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException($"No se pudo iniciar {spec.DisplayCommand}.");

        using var timeoutCts = new CancellationTokenSource(spec.Timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TryKillProcessTree(process);
        }

        var stdout = await SafeReadAsync(stdoutTask);
        var stderr = await SafeReadAsync(stderrTask);
        var exitCode = process.HasExited ? process.ExitCode : -1;
        var output = Limit(string.Join(
            Environment.NewLine,
            new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))));

        return new ElevatedRunnerResult(exitCode, timedOut, output);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Mejor esfuerzo: el timeout se reporta y no se intenta ninguna otra acción.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Limit(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= MaximumOutputCharacters)
            return value ?? string.Empty;

        return "…[" + value[^MaximumOutputCharacters..];
    }
}
