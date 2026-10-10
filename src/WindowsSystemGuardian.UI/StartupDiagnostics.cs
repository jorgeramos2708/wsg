using System.Text;

namespace WindowsSystemGuardian.UI;

internal static class StartupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogDirectory { get; } = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "WindowsSystemGuardian",
        "Logs");

    public static string LogPath { get; } = System.IO.Path.Combine(LogDirectory, "startup.log");

    public static void Initialize()
    {
        try
        {
            System.IO.Directory.CreateDirectory(LogDirectory);
            System.IO.File.WriteAllText(
                LogPath,
                $"Windows System Guardian startup diagnostic{System.Environment.NewLine}" +
                $"Started: {DateTimeOffset.Now:O}{System.Environment.NewLine}" +
                $"OS: {System.Environment.OSVersion}{System.Environment.NewLine}" +
                $"64-bit process: {System.Environment.Is64BitProcess}{System.Environment.NewLine}" +
                $"Runtime: {System.Environment.Version}{System.Environment.NewLine}{System.Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Startup diagnostics must never prevent the application from launching.
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                System.IO.File.AppendAllText(
                    LogPath,
                    $"[{DateTimeOffset.Now:O}] {message}{System.Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics are best effort only.
        }
    }

    public static void WriteException(string context, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                System.IO.File.AppendAllText(
                    LogPath,
                    $"[{DateTimeOffset.Now:O}] {context}{System.Environment.NewLine}" +
                    $"{exception}{System.Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics are best effort only.
        }
    }
}
