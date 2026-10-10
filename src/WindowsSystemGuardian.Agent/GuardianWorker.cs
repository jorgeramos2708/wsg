using WindowsSystemGuardian.Core.Scheduling;

namespace WindowsSystemGuardian.Agent;

public sealed class GuardianWorker(
    ILogger<GuardianWorker> logger,
    DiagnosticScheduler scheduler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Windows System Guardian Agent {Version} iniciado.", typeof(GuardianWorker).Assembly.GetName().Version?.ToString(3));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await scheduler.RunDueAsync(stoppingToken);
                if (snapshot is not null)
                {
                    logger.LogInformation(
                        "Observabilidad: score {Score}, {Warnings} advertencias, {Critical} críticos, {Incidents} incidentes, {Collectors} colectores.",
                        snapshot.HealthScore,
                        snapshot.WarningCount,
                        snapshot.CriticalCount,
                        snapshot.Incidents.Count,
                        snapshot.Collectors.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo completar el ciclo de observabilidad.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
