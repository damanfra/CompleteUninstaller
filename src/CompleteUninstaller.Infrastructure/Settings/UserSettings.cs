using System.Text;
using System.Text.Json;
using CompleteUninstaller.Infrastructure.Logging;

namespace CompleteUninstaller.Infrastructure.Settings;

/// <summary>Últimas opções escolhidas na tela de desinstalação.</summary>
public sealed class UninstallOptions
{
    public bool Moderate { get; set; }

    public bool CreateRestorePoint { get; set; } = true;

    public bool PreferQuiet { get; set; }
}

/// <summary>Preferências do usuário, guardadas em %LocalAppData%\CompleteUninstaller\settings.json.</summary>
public sealed class UserSettings
{
    public UninstallOptions Uninstall { get; set; } = new();
}

/// <summary>Lê e grava as preferências. Arquivo ausente ou ilegível volta aos padrões, sem erro.</summary>
public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UserSettingsStore(string? filePath = null) =>
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompleteUninstaller",
            "settings.json");

    public string FilePath { get; }

    public UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new UserSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Preferências ilegíveis, usando os padrões: {FilePath} ({ex.Message})");
        }

        return new UserSettings();
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Não foi possível gravar as preferências: {FilePath} ({ex.Message})");
        }
    }
}
