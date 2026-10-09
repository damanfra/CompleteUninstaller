using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using Microsoft.Win32;
using static CompleteUninstaller.Infrastructure.Inventory.ValueParsers;

namespace CompleteUninstaller.Infrastructure.Inventory;

/// <summary>
/// Lê as chaves de desinstalação do Registro: HKLM (64 e 32 bits) e o HKCU de todos os usuários
/// com sessão carregada (HKEY_USERS\SID). Inclui as entradas que o Painel de Controle esconde.
/// </summary>
internal sealed class RegistryUninstallSource
{
    internal const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallSubKeyWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public List<InstalledApp> Enumerate(List<string> errors)
    {
        var apps = new List<InstalledApp>();
        ReadMachine(RegistryView.Registry64, RegistryScope.Machine64, apps, errors);
        if (Environment.Is64BitOperatingSystem)
        {
            ReadMachine(RegistryView.Registry32, RegistryScope.Machine32, apps, errors);
        }

        ReadUsers(apps, errors);
        return apps;
    }

    private static void ReadMachine(RegistryView view, RegistryScope scope, List<InstalledApp> apps, List<string> errors)
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var root = hklm.OpenSubKey(UninstallSubKey);
            if (root is null)
            {
                return;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                var location = new RegistryLocation("HKLM", view == RegistryView.Registry32, $@"{UninstallSubKey}\{name}");
                var app = ReadEntry(root, name, scope, location, null);
                if (app is not null)
                {
                    apps.Add(app);
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Registro (HKLM {view}): {ex.Message}");
            Log.Error($"Falha lendo HKLM {view}", ex);
        }
    }

    private static void ReadUsers(List<InstalledApp> apps, List<string> errors)
    {
        try
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            foreach (var sid in users.GetSubKeyNames())
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
                    || sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var subKey in new[] { UninstallSubKey, UninstallSubKeyWow })
                {
                    using var root = users.OpenSubKey($@"{sid}\{subKey}");
                    if (root is null)
                    {
                        continue;
                    }

                    foreach (var name in root.GetSubKeyNames())
                    {
                        var location = new RegistryLocation("HKU", false, $@"{sid}\{subKey}\{name}");
                        var app = ReadEntry(root, name, RegistryScope.User, location, sid);
                        if (app is not null)
                        {
                            apps.Add(app);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Registro (usuários): {ex.Message}");
            Log.Error("Falha lendo HKEY_USERS", ex);
        }
    }

    private static InstalledApp? ReadEntry(RegistryKey parent, string keyName, RegistryScope scope, RegistryLocation location, string? sid)
    {
        try
        {
            using var key = parent.OpenSubKey(keyName);
            if (key is null)
            {
                return null;
            }

            var displayName = GetString(key, "DisplayName");
            if (displayName is null)
            {
                return null; // entradas sem nome são componentes internos de instaladores
            }

            var flags = AppFlags.None;
            if (GetInt(key, "SystemComponent") == 1)
            {
                flags |= AppFlags.SystemComponent;
            }

            if (GetInt(key, "NoRemove") == 1)
            {
                flags |= AppFlags.NoRemove;
            }

            var releaseType = GetString(key, "ReleaseType") ?? string.Empty;
            if (GetString(key, "ParentKeyName") is not null
                || releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)
                || releaseType.Contains("Service Pack", StringComparison.OrdinalIgnoreCase))
            {
                flags |= AppFlags.Update;
            }

            var isMsi = GetInt(key, "WindowsInstaller") == 1;
            var sizeKb = GetInt(key, "EstimatedSize");

            return new InstalledApp
            {
                Id = $"reg|{location}",
                DisplayName = displayName,
                Source = AppSource.Registry,
                Scope = scope,
                DisplayVersion = GetString(key, "DisplayVersion"),
                Publisher = GetString(key, "Publisher"),
                InstallLocation = CleanPath(GetString(key, "InstallLocation")),
                InstallDate = ParseDate(GetString(key, "InstallDate")),
                EstimatedSizeBytes = sizeKb is > 0 ? sizeKb.Value * 1024L : null,
                UninstallString = GetString(key, "UninstallString"),
                QuietUninstallString = GetString(key, "QuietUninstallString"),
                DisplayIcon = GetString(key, "DisplayIcon"),
                BundleCachePath = CleanPath(GetString(key, "BundleCachePath")),
                IsWindowsInstaller = isMsi,
                MsiProductCode = isMsi && CommandLineParser.IsGuid(keyName) ? keyName.ToUpperInvariant() : null,
                UninstallKey = location,
                UserSid = sid,
                Flags = flags,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Entrada ilegível {location}: {ex.Message}");
            return null;
        }
    }
}
