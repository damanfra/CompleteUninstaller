using CompleteUninstaller.Infrastructure.Inventory;
using CompleteUninstaller.Infrastructure.Leftovers;
using CompleteUninstaller.Infrastructure.Platform;
using CompleteUninstaller.Infrastructure.Removal;
using CompleteUninstaller.Infrastructure.Uninstall;
using CompleteUninstaller.Updater;

namespace CompleteUninstaller.App;

/// <summary>Composição dos serviços (sem contêiner de DI para manter o projeto enxuto).</summary>
public sealed class AppServices
{
    public required SystemPaths Paths { get; init; }

    public required InventoryService Inventory { get; init; }

    public required UninstallRunner Uninstaller { get; init; }

    public required LeftoverScanner Scanner { get; init; }

    public required RemovalService Removal { get; init; }

    public required QuarantineStore Quarantine { get; init; }

    public required RestorePointService RestorePoints { get; init; }

    public required UpdateService Updates { get; init; }

    public static AppServices Create()
    {
        var paths = SystemPaths.Detect();
        var quarantine = new QuarantineStore();
        return new AppServices
        {
            Paths = paths,
            Inventory = new InventoryService(),
            Uninstaller = new UninstallRunner(),
            Scanner = new LeftoverScanner(paths),
            Removal = new RemovalService(quarantine, paths),
            Quarantine = quarantine,
            RestorePoints = new RestorePointService(),
            Updates = new UpdateService(),
        };
    }
}
