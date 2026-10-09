using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace CompleteUninstaller.Infrastructure.Inventory;

/// <summary>Pacotes AppX/MSIX (Microsoft Store e apps empacotados) do usuário atual.</summary>
internal sealed class AppxSource
{
    public List<InstalledApp> Enumerate(List<string> errors)
    {
        var result = new List<InstalledApp>();
        try
        {
            var manager = new PackageManager();
            var provisioned = FindProvisionedFamilies(manager);
            foreach (var package in manager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Main))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage)
                    {
                        continue;
                    }

                    var id = package.Id;
                    var displayName = SafeGet(() => package.DisplayName);
                    if (string.IsNullOrWhiteSpace(displayName)
                        || displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                    {
                        displayName = id.Name;
                    }

                    var flags = AppFlags.None;
                    if (SafeGet(() => package.SignatureKind) == PackageSignatureKind.System)
                    {
                        flags |= AppFlags.SystemComponent;
                    }

                    if (provisioned.Contains(id.FamilyName))
                    {
                        flags |= AppFlags.Provisioned;
                    }

                    var version = id.Version;
                    result.Add(new InstalledApp
                    {
                        Id = $"appx|{id.FullName}",
                        DisplayName = displayName,
                        Source = AppSource.Store,
                        DisplayVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                        Publisher = SafeGet(() => package.PublisherDisplayName),
                        InstallLocation = SafeGet(() => package.InstalledPath),
                        InstallDate = SafeGet<DateTime?>(() => package.InstalledDate.LocalDateTime),
                        PackageFullName = id.FullName,
                        PackageFamilyName = id.FamilyName,
                        LogoPath = SafeGet(() => package.Logo?.LocalPath),
                        Flags = flags,
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn($"Pacote ignorado: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Apps da Store: {ex.Message}");
            Log.Error("Falha enumerando pacotes AppX", ex);
        }

        return result;
    }

    /// <summary>Famílias de pacotes que vêm na imagem do Windows (exige administrador; vazio se falhar).</summary>
    private static HashSet<string> FindProvisionedFamilies(PackageManager manager)
    {
        try
        {
            return manager.FindProvisionedPackages()
                .Select(p => p.Id.FamilyName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível listar os pacotes pré-instalados do Windows: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static T? SafeGet<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }
}
