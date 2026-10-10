using System.Text;
using System.Windows;

namespace WindowsSystemGuardian.UI;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        StartupDiagnostics.Initialize();
        StartupDiagnostics.Write("Process entry point reached.");

        try
        {
            var app = new App();
            StartupDiagnostics.Write("App instance created.");

            app.InitializeComponent();
            StartupDiagnostics.Write("App.xaml resources initialized.");

            app.Run();
            StartupDiagnostics.Write("Application message loop exited normally.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("Fatal startup exception", ex);

            try
            {
                MessageBox.Show(
                    BuildErrorMessage(ex),
                    "Windows System Guardian - Error de inicio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // If WPF itself cannot display a dialog, the startup log remains available.
            }
        }
    }

    private static string BuildErrorMessage(Exception ex)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Windows System Guardian no pudo iniciar correctamente.");
        builder.AppendLine();
        builder.AppendLine(ex.GetBaseException().Message);
        builder.AppendLine();
        builder.AppendLine("Se guardó un diagnóstico en:");
        builder.AppendLine(StartupDiagnostics.LogPath);
        return builder.ToString();
    }
}
