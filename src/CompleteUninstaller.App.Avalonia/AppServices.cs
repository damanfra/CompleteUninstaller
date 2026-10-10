using CompleteUninstaller.Infrastructure.Linux;
using CompleteUninstaller.Infrastructure.Linux.Inventory;
using CompleteUninstaller.Infrastructure.Linux.Leftovers;
using CompleteUninstaller.Infrastructure.Linux.Removal;
using CompleteUninstaller.Infrastructure.Linux.Uninstall;
using CompleteUninstaller.Updater;

namespace CompleteUninstaller.App.Avalonia;

/// <summary>Composição dos serviços (sem contêiner de DI para manter o projeto enxuto).</summary>
public sealed class AppServices
{
    public required LinuxEnvironment Environment { get; init; }

    public required LinuxInventoryService Inventory { get; init; }

    public required LinuxUninstallRunner Uninstaller { get; init; }

    public required LinuxLeftoverScanner Scanner { get; init; }

    public required RemovalService Removal { get; init; }

    public required QuarantineStore Quarantine { get; init; }

    public required UpdateService Updates { get; init; }

    public static AppServices Create()
    {
        var environment = LinuxEnvironment.Detect();
        var helper = new HelperClient(environment);
        var quarantine = new QuarantineStore(environment, helper);
        return new AppServices
        {
            Environment = environment,
            Inventory = new LinuxInventoryService(environment),
            Uninstaller = new LinuxUninstallRunner(environment),
            Scanner = new LinuxLeftoverScanner(environment),
            Removal = new RemovalService(environment, quarantine, helper),
            Quarantine = quarantine,
            Updates = new UpdateService(),
        };
    }
}
