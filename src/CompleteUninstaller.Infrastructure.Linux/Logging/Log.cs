namespace CompleteUninstaller.Infrastructure.Linux.Logging;

/// <summary>
/// Log simples em arquivo (~/.local/share/CompleteUninstaller/Logs). Registra tudo o que foi
/// executado, encontrado, ignorado pela proteção e removido.
/// </summary>
public static class Log
{
    private static readonly object Sync = new();

    public static string CurrentFile => Path.Combine(AppPaths.LogsRoot, $"log-{DateTime.Now:yyyyMMdd}.txt");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("AVISO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERRO", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.LogsRoot);
                File.AppendAllText(
                    CurrentFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // O log nunca pode derrubar o aplicativo.
        }
    }
}
