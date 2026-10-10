using WindowsSystemGuardian.Core.Diagnostics;

namespace WindowsSystemGuardian.Core.Tests;

public sealed class DiagnosticInterpretersTests
{
    /// <summary>Salida real capturada en Windows 11 (idioma español) durante la auditoría MSDoc.</summary>
    private const string RealReagentcSpanish = """
        Información de configuración del Entorno de recuperación de Windows (Windows RE)
        Información:

            Estado de Windows RE:                  Enabled
            Ubicación de Windows RE:               \\?\GLOBALROOT\device\harddisk0\partition4\Recovery\WindowsRE
            Identificador de datos de la configuración de arranque (BCD): b69b7ea9-53a8-11f0-9fd8-c484bfbc121f
            Ubicación de imagen para recuperación:
            Índice de imagen para recuperación:    0
            Versión de Windows RE:        10.0.26100.8875

        REAGENTC.EXE: operación efectuada correctamente.
        """;

    /// <summary>Salida real capturada en Windows 11 (idioma español): writers con estado estable y uno transitorio.</summary>
    private const string RealVssWritersSpanish = """
        vssadmin 1.1 - Herramienta administrativa de línea de comandos del Servicio de instantáneas de volumen.

        Nombre de editor: 'Task Scheduler Writer'
           Id. de editor: {d61d61c8-d73a-4eee-8cdd-f6f9786b7124}
           Estado: [1] Estable
           Último error: Sin errores

        Nombre de editor: 'System Writer'
           Id. de editor: {e8132975-6f93-4464-a53e-1050253ae220}
           Estado: [5] Esperando finalización
           Último error: Sin errores
        """;

    [Theory]
    [InlineData("    Estado de Windows RE:                  Enabled")]
    [InlineData("    Windows RE status:         Enabled")]
    [InlineData("Windows RE status: Enabled")]
    [InlineData("    Estado de Windows RE:    Habilitado")]
    public void WinRe_EnabledVariants_ParseAsEnabled(string output)
    {
        Assert.Equal(WinReStatus.Enabled, WinReStatusInterpreter.Parse(output));
    }

    [Theory]
    [InlineData("    Estado de Windows RE:                  Disabled")]
    [InlineData("    Windows RE status: Disabled")]
    [InlineData("    Estado de Windows RE: Deshabilitado")]
    public void WinRe_DisabledVariants_ParseAsDisabled(string output)
    {
        Assert.Equal(WinReStatus.Disabled, WinReStatusInterpreter.Parse(output));
    }

    [Fact]
    public void WinRe_RealSpanishOutput_ParsesEnabled()
    {
        Assert.Equal(WinReStatus.Enabled, WinReStatusInterpreter.Parse(RealReagentcSpanish));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Este comando solo puede ejecutarse desde un símbolo del sistema con privilegios elevados.")]
    [InlineData("REAGENTC.EXE: error en la operación: 5")]
    [InlineData("    Windows RE status:         0")]
    public void WinRe_UnrecognizedOutput_IsUnparseable(string output)
    {
        // La variante "0" numérica queda expresamente fuera: no es un formato
        // confirmado por la documentación ni por evidencia; fail-closed ante la duda.
        Assert.Equal(WinReStatus.Unparseable, WinReStatusInterpreter.Parse(output));
    }

    [Fact]
    public void Vss_RealHealthySystem_TransientStatesAreNotFailures()
    {
        // Hallazgo H-2: estado [5] transitorio en un sistema sano no debe producir failed.
        var states = new[] { 1, 1, 1, 5 };

        var classification = VssWriterStateInterpreter.Classify(states);

        Assert.Equal(4, classification.Total);
        Assert.Equal(3, classification.Stable);
        Assert.Equal(1, classification.Transient);
        Assert.Equal(0, classification.Failed);
    }

    [Fact]
    public void Vss_FailedStates_AreClassifiedAsFailures()
    {
        // Estados Failed* del enum VSS_WRITER_STATE empiezan en 9; 0 es undefined.
        var states = new[] { 1, 9, 12, 0 };

        var classification = VssWriterStateInterpreter.Classify(states);

        Assert.Equal(4, classification.Total);
        Assert.Equal(1, classification.Stable);
        Assert.Equal(0, classification.Transient);
        Assert.Equal(3, classification.Failed);
    }

    [Fact]
    public void Vss_AllTransient_ClassifiesWithoutFailures()
    {
        var states = new[] { 2, 3, 4, 5, 6, 7, 8 };

        var classification = VssWriterStateInterpreter.Classify(states);

        Assert.Equal(7, classification.Transient);
        Assert.Equal(0, classification.Failed);
        Assert.Equal(0, classification.Stable);
    }

    [Fact]
    public void Vss_EmptyStates_IsAllZero()
    {
        var classification = VssWriterStateInterpreter.Classify([]);

        Assert.Equal(0, classification.Total);
        Assert.Equal(0, classification.Stable);
        Assert.Equal(0, classification.Transient);
        Assert.Equal(0, classification.Failed);
    }
}
