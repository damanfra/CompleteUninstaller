using CompleteUninstaller.Core.Inventory;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Native;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace CompleteUninstaller.Infrastructure.Inventory;

public sealed record InventoryResult(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<string> Errors);

/// <summary>Monta a lista unificada de programas instalados a partir de todas as fontes.</summary>
public sealed class InventoryService
{
    public Task<InventoryResult> LoadAsync() => Task.Run(() => Load());

    public InventoryResult Load()
    {
        var errors = new List<string>();
        var registry = new RegistryUninstallSource().Enumerate(errors);
        var msi = new MsiSource().Enumerate(errors);
        var store = new AppxSource().Enumerate(errors);
        var apps = InventoryMerger.Merge(registry, msi, store);
        Log.Info($"Inventário: {apps.Count} itens (Registro {registry.Count}, MSI {msi.Count}, Store {store.Count}).");
        return new InventoryResult(apps, errors);
    }

    /// <summary>Confere se o programa continua registrado (usado depois de rodar o desinstalador).</summary>
    public bool IsStillInstalled(InstalledApp app)
    {
        try
        {
            if (app.Source == AppSource.Store)
            {
                return app.PackageFullName is not null
                    && new PackageManager().FindPackageForUser(string.Empty, app.PackageFullName) is not null;
            }

            if (app.Source == AppSource.Registry && app.UninstallKey is not null)
            {
                return RegistryKeyExists(app.UninstallKey);
            }

            return app.MsiProductCode is not null
                && NativeMethods.MsiQueryProductState(app.MsiProductCode) == NativeMethods.INSTALLSTATE_DEFAULT;
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível verificar se '{app.DisplayName}' ainda está instalado: {ex.Message}");
            return false;
        }
    }

    private static bool RegistryKeyExists(RegistryLocation location)
    {
        var hive = location.Hive == "HKU" ? RegistryHive.Users : RegistryHive.LocalMachine;
        var view = location.Is32BitView ? RegistryView.Registry32 : RegistryView.Registry64;
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(location.SubKeyPath);
        return key is not null;
    }
}
