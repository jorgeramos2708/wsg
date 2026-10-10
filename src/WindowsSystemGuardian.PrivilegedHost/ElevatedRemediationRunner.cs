using System.Diagnostics;
using System.Text;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.PrivilegedHost;

internal sealed record ElevatedRunnerResult(
    int ExitCode,
    bool TimedOut,
    string Output);

/// <summary>
/// Ejecuta exclusivamente los comandos del catálogo de remediación (DISM /RestoreHealth,
/// SFC /scannow), resueltos desde System32, con timeout, árbol de procesos bajo control
/// y salida acotada. Mismo patrón que ReadOnlyDiagnosticProcessRunner de 0.4.1; no
/// contiene switches de reparación distintos de los dos soportados.
/// </summary>
internal sealed class ElevatedRemediationRunner
{
    private const int MaximumOutputCharacters = 8_192;

    public async Task<ElevatedRunnerResult> RunAsync(RemediationActionId action)
    {
        var spec = RemediationCommandCatalog.For(action);
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
