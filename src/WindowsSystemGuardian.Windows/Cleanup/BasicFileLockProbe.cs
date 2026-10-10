using WindowsSystemGuardian.Core.Abstractions;

namespace WindowsSystemGuardian.Windows.Cleanup;

public sealed class BasicFileLockProbe : IFileLockProbe
{
    public bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.SequentialScan);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Si no podemos demostrar que es accesible de forma segura, se trata como bloqueado.
            return true;
        }
    }
}
