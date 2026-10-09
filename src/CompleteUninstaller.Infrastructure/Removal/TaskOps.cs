using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Removal;

/// <summary>Remove e recria tarefas agendadas via schtasks.exe (o XML original fica guardado na quarentena).</summary>
internal static class TaskOps
{
    public static void Delete(string taskPath)
    {
        var result = ProcessRunner.Run("schtasks.exe", ["/Delete", "/TN", taskPath, "/F"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"schtasks /Delete falhou (código {result.ExitCode}): {result.Output}");
        }
    }

    public static void Recreate(string taskPath, string xmlFile)
    {
        var result = ProcessRunner.Run("schtasks.exe", ["/Create", "/TN", taskPath, "/XML", xmlFile, "/F"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"schtasks /Create falhou (código {result.ExitCode}): {result.Output}");
        }
    }
}
