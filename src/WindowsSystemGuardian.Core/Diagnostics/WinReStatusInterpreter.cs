using System.Text.RegularExpressions;

namespace WindowsSystemGuardian.Core.Diagnostics;

/// <summary>Estado de Windows RE interpretado de la salida de reagentc /info.</summary>
public enum WinReStatus
{
    /// <summary>No se pudo interpretar: fail-closed, el collector reporta Failure.</summary>
    Unparseable = 0,
    Enabled,
    Disabled
}

/// <summary>
/// Interpreta la salida de <c>reagentc /info</c>. Verificado empíricamente contra la
/// salida real (Windows 11, idioma español): la línea de estado usa etiqueta en español
/// y valor en inglés, con espaciado variable:
/// <code>
///     Estado de Windows RE:                  Enabled
/// </code>
/// En Windows en inglés: <c>Windows RE status:         Enabled</c>. La documentación
/// oficial de REAgentC (learn.microsoft.com/windows-hardware/manufacture/desktop/reagentc-command-line-options)
/// no documenta el formato exacto de salida, por lo que el parser es tolerante al
/// espaciado y bilingüe; ante cualquier otra forma devuelve Unparseable (fail-closed).
/// </summary>
public static class WinReStatusInterpreter
{
    private static readonly Regex StatusRegex = new(
        @"(?im)^\s*(?:Windows\s+RE\s+status|Estado\s+de\s+Windows\s+RE)\s*:\s*(?<value>Enabled|Habilitado|Disabled|Deshabilitado)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static WinReStatus Parse(string? output)
    {
        var match = StatusRegex.Match(output ?? string.Empty);
        if (!match.Success)
            return WinReStatus.Unparseable;

        var value = match.Groups["value"].Value;
        return value.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Habilitado", StringComparison.OrdinalIgnoreCase)
            ? WinReStatus.Enabled
            : WinReStatus.Disabled;
    }
}
