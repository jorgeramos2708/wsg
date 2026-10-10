using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.PrivilegedHost;

/// <summary>
/// Host elevado de remediación de WSG 0.5.0. Ejecuta exclusivamente los comandos del
/// catálogo soportado, escribe un resultado estructurado JSON y sale. No acepta
/// ejecutables, argumentos ni rutas de comando arbitrarias.
///
/// Exit codes: 0 completado (veredicto en JSON), 2 argumentos inválidos,
/// 3 timeout de ejecución, 4 fallo al lanzar o completar la ejecución.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        if (!ElevatedHostLaunchSpec.TryParse(args, out var invocation, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!Directory.Exists(Path.GetDirectoryName(invocation.OutputFilePath)))
        {
            Console.Error.WriteLine("El directorio de salida del resultado no existe.");
            return 2;
        }

        try
        {
            var runner = new ElevatedRemediationRunner();
            var result = await runner.RunAsync(invocation.Action);

            var verdict = RemediationOutcomeParser.Parse(
                invocation.Action,
                result.ExitCode,
                result.TimedOut,
                result.Output);

            var outcome = new RemediationOutcome(
                RemediationOutcome.CurrentSchemaVersion,
                invocation.Action,
                invocation.RequestId,
                result.ExitCode,
                result.TimedOut,
                result.Output,
                verdict,
                DateTime.UtcNow);

            await File.WriteAllTextAsync(invocation.OutputFilePath, JsonSerializer.Serialize(outcome, JsonOptions));

            return result.TimedOut ? 3 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fallo de la ejecución elevada: {ex.Message}");
            return 4;
        }
    }
}
