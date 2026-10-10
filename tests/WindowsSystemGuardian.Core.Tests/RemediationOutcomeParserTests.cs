using WindowsSystemGuardian.Contracts.Remediation;
using WindowsSystemGuardian.Core.Remediation;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class RemediationOutcomeParserTests
{
    [Fact]
    public void Dism_RestoreCompletedEnglish_IsRepaired()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.ComponentStoreRepair,
            exitCode: 0,
            timedOut: false,
            output: "The restore operation completed successfully.\nThe operation completed.");

        Assert.Equal(RemediationParsedVerdict.Repaired, verdict);
    }

    [Fact]
    public void Dism_RestoreCompletedSpanish_IsRepaired()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.ComponentStoreRepair,
            exitCode: 0,
            timedOut: false,
            output: "La operación de restauración se completó correctamente.");

        Assert.Equal(RemediationParsedVerdict.Repaired, verdict);
    }

    [Fact]
    public void Dism_NoCorruptionEnglish_IsNoDamageFound()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.ComponentStoreRepair,
            exitCode: 0,
            timedOut: false,
            output: "No component store corruption detected.");

        Assert.Equal(RemediationParsedVerdict.NoDamageFound, verdict);
    }

    [Fact]
    public void Dism_NoCorruptionSpanish_IsNoDamageFound()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.ComponentStoreRepair,
            exitCode: 0,
            timedOut: false,
            output: "No se detectó ninguna corrupción en el almacén de componentes.");

        Assert.Equal(RemediationParsedVerdict.NoDamageFound, verdict);
    }

    [Fact]
    public void Dism_SuccessfulExitWithUnknownText_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.ComponentStoreRepair,
            exitCode: 0,
            timedOut: false,
            output: "Texto no reconocido por el parser.");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void Dism_NonZeroExit_IsIndeterminateRegardlessOfText()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.ComponentStoreRepair,
            exitCode: 87,
            timedOut: false,
            output: "The restore operation completed successfully.");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void TimedOut_IsIndeterminateEvenWithSuccessText()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: true,
            output: "Windows Resource Protection found corrupt files and successfully repaired them.");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void CancelledText_IsCancelled()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: -1,
            timedOut: false,
            output: "La operación cancelada por el usuario.");

        Assert.Equal(RemediationParsedVerdict.Cancelled, verdict);
    }

    [Fact]
    public void Sfc_RepairedEnglish_IsRepaired()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: "Windows Resource Protection found corrupt files and successfully repaired them.");

        Assert.Equal(RemediationParsedVerdict.Repaired, verdict);
    }

    [Fact]
    public void Sfc_RepairedSpanish_IsRepaired()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: "Protección de recursos de Windows encontró archivos dañados y los reparó correctamente.");

        Assert.Equal(RemediationParsedVerdict.Repaired, verdict);
    }

    [Fact]
    public void Sfc_NoViolationsEnglish_IsNoDamageFound()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: "Windows Resource Protection did not find any integrity violations.");

        Assert.Equal(RemediationParsedVerdict.NoDamageFound, verdict);
    }

    [Fact]
    public void Sfc_NoViolationsSpanish_IsNoDamageFound()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: "Protección de recursos de Windows no encontró ninguna violación de integridad.");

        Assert.Equal(RemediationParsedVerdict.NoDamageFound, verdict);
    }

    [Fact]
    public void Sfc_PartialRepair_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: "Windows Resource Protection found corrupt files but was unable to fix some of them.");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void Sfc_PartialRepairSpanish_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: "CBS no tuvo la capacidad de corregir todos los archivos dañados.");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void EmptyOutput_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.SystemFileRepair,
            exitCode: 0,
            timedOut: false,
            output: string.Empty);

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void WuRestart_SuccessMarker_IsRepaired()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.WindowsUpdateServiceRestart,
            exitCode: 0,
            timedOut: false,
            output: "Operación: Reinicio controlado de los servicios Windows Update (bits, wuauserv)\n[stop] bits: detenido.\n[stop] wuauserv: detenido.\n[start] wuauserv: en ejecución.\n[start] bits: en ejecución.\nWU_RESTART_OK");

        Assert.Equal(RemediationParsedVerdict.Repaired, verdict);
    }

    [Fact]
    public void WuRestart_FailureOutput_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.WindowsUpdateServiceRestart,
            exitCode: 4,
            timedOut: false,
            output: "[fallo] No se pudo abrir el servicio wuauserv");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void WuRestart_SuccessExitWithoutMarker_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.WindowsUpdateServiceRestart,
            exitCode: 0,
            timedOut: false,
            output: "pasos incompletos, sin marcador");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }

    [Fact]
    public void UnsupportedAction_IsIndeterminate()
    {
        var verdict = RemediationOutcomeParser.Parse(
            RemediationActionId.None,
            exitCode: 0,
            timedOut: false,
            output: "The restore operation completed successfully.");

        Assert.Equal(RemediationParsedVerdict.Indeterminate, verdict);
    }
}
