namespace CompleteUninstaller.Infrastructure;

/// <summary>Pastas do próprio Complete Uninstaller.</summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CompleteUninstaller");

    public static string QuarantineRoot => Path.Combine(DataRoot, "Quarantine");

    public static string LogsRoot => Path.Combine(DataRoot, "Logs");
}
