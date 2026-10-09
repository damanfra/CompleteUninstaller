using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.Core.Inventory;

/// <summary>
/// Junta as listas de cada fonte, eliminando duplicatas:
/// um produto MSI que já tem chave no Registro vira um só item (enriquecido);
/// os que não têm ficam marcados como "não listados pelo Windows".
/// </summary>
public static class InventoryMerger
{
    public static List<InstalledApp> Merge(
        IEnumerable<InstalledApp> registryApps,
        IEnumerable<InstalledApp> msiApps,
        IEnumerable<InstalledApp> storeApps)
    {
        var result = new List<InstalledApp>();
        var byProductCode = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in registryApps)
        {
            // A mesma entrada às vezes aparece nas visões de 32 e 64 bits.
            var key = $"{app.UserSid}|{NameNormalizer.Normalize(app.DisplayName)}|{app.DisplayVersion}|{app.UninstallString}";
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(app);
            if (app.MsiProductCode is { } code)
            {
                byProductCode.TryAdd(code, app);
            }

            if (app.UninstallKey is { } location)
            {
                var leaf = WinPath.GetLeaf(location.SubKeyPath);
                if (CommandLineParser.IsGuid(leaf))
                {
                    byProductCode.TryAdd(leaf, app);
                }
            }
        }

        foreach (var msi in msiApps)
        {
            if (msi.MsiProductCode is { } code && byProductCode.TryGetValue(code, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.InstallLocation))
                {
                    existing.InstallLocation = msi.InstallLocation;
                }

                if (string.IsNullOrWhiteSpace(existing.Publisher))
                {
                    existing.Publisher = msi.Publisher;
                }

                existing.MsiProductCode ??= msi.MsiProductCode;
                continue;
            }

            msi.Flags |= AppFlags.NotListedByWindows;
            result.Add(msi);
            if (msi.MsiProductCode is { } newCode)
            {
                byProductCode.TryAdd(newCode, msi);
            }
        }

        result.AddRange(storeApps);
        return result
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
