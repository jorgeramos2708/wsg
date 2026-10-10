namespace WindowsSystemGuardian.Core.Abstractions;

public interface IFileLockProbe
{
    bool IsLocked(string path);
}
