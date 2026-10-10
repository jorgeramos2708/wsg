using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowsSystemGuardian.Windows.Diagnostics;

internal enum ReadOnlyDiagnosticCommand
{
    DismCheckHealth,
    DismScanHealth,
    SfcVerifyOnly,
    ReAgentInfo,
    VssListWriters,
    ChkdskScan
}

internal sealed record ReadOnlyDiagnosticProcessResult(
    ReadOnlyDiagnosticCommand Command,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut)
{
    public string CombinedOutput => string.Join(
        global::System.Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Único punto autorizado para procesos CLI de diagnóstico en WSG 0.4.1.
/// No acepta ejecutables ni argumentos arbitrarios: cada comando está fijado en
/// código y todos son de consulta/verificación. No contiene switches de reparación.
/// </summary>
internal sealed class ReadOnlyDiagnosticProcessRunner
{
    private static readonly Regex DriveLetterPattern = new("^[A-Za-z]:$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ReadOnlyDiagnosticProcessResult> RunAsync(
        ReadOnlyDiagnosticCommand command,
        string? validatedDriveLetter = null,
        CancellationToken cancellationToken = default)
    {
        var spec = BuildSpecification(command, validatedDriveLetter);
        var started = DateTime.UtcNow;

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.Executable,
            Arguments = spec.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = global::System.Environment.SystemDirectory
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException($"No se pudo iniciar el diagnóstico {command}.");

        // La misma ventana de timeout gobierna WaitForExit y el drenaje de stdout/stderr.
        // Así, incluso si Kill() falla, WSG no puede quedar bloqueado esperando una CLI.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(spec.Timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort: el timeout se reporta y no se intenta ninguna acción adicional.
            }
        }

        if (cancellationToken.IsCancellationRequested)
            cancellationToken.ThrowIfCancellationRequested();

        var stdout = await SafeReadAsync(stdoutTask);
        var stderr = await SafeReadAsync(stderrTask);
        var duration = DateTime.UtcNow - started;
        var exitCode = process.HasExited ? process.ExitCode : -1;

        return new ReadOnlyDiagnosticProcessResult(
            command,
            exitCode,
            Limit(stdout),
            Limit(stderr),
            duration,
            timedOut);
    }

    private static DiagnosticCommandSpecification BuildSpecification(
        ReadOnlyDiagnosticCommand command,
        string? driveLetter)
    {
        var system32 = global::System.Environment.SystemDirectory;
        return command switch
        {
            ReadOnlyDiagnosticCommand.DismCheckHealth => new(
                Path.Combine(system32, "dism.exe"),
                "/Online /Cleanup-Image /CheckHealth /NoRestart /English",
                TimeSpan.FromMinutes(5)),

            ReadOnlyDiagnosticCommand.DismScanHealth => new(
                Path.Combine(system32, "dism.exe"),
                "/Online /Cleanup-Image /ScanHealth /NoRestart /English",
                TimeSpan.FromMinutes(30)),

            ReadOnlyDiagnosticCommand.SfcVerifyOnly => new(
                Path.Combine(system32, "sfc.exe"),
                "/verifyonly",
                TimeSpan.FromMinutes(45)),

            ReadOnlyDiagnosticCommand.ReAgentInfo => new(
                Path.Combine(system32, "reagentc.exe"),
                "/info",
                TimeSpan.FromMinutes(2)),

            ReadOnlyDiagnosticCommand.VssListWriters => new(
                Path.Combine(system32, "vssadmin.exe"),
                "list writers",
                TimeSpan.FromMinutes(3)),

            ReadOnlyDiagnosticCommand.ChkdskScan => new(
                Path.Combine(system32, "chkdsk.exe"),
                $"{ValidateDriveLetter(driveLetter)} /scan",
                TimeSpan.FromMinutes(45)),

            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };
    }

    private static string ValidateDriveLetter(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter) || !DriveLetterPattern.IsMatch(driveLetter))
            throw new ArgumentException("El diagnóstico CHKDSK solo acepta una letra de unidad local validada, por ejemplo C:.", nameof(driveLetter));

        return driveLetter.ToUpperInvariant();
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try { return await task; }
        catch { return string.Empty; }
    }

    private static string Limit(string value)
    {
        const int maxChars = 64 * 1024;
        if (value.Length <= maxChars)
            return value.Trim();
        return value[..maxChars].Trim() + global::System.Environment.NewLine + "[salida truncada por WSG]";
    }

    private sealed record DiagnosticCommandSpecification(
        string Executable,
        string Arguments,
        TimeSpan Timeout);
}
