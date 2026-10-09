using CompleteUninstaller.Core.Inventory;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.Core.Tests;

public class InventoryMergerTests
{
    private const string Code = "{11111111-2222-3333-4444-555555555555}";

    [Fact]
    public void Msi_product_with_registry_entry_is_merged()
    {
        var registry = new InstalledApp
        {
            Id = "reg",
            DisplayName = "Foo",
            Source = AppSource.Registry,
            IsWindowsInstaller = true,
            MsiProductCode = Code,
            UninstallKey = new RegistryLocation("HKLM", false, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{Code}"),
        };
        var msi = new InstalledApp
        {
            Id = "msi",
            DisplayName = "Foo",
            Source = AppSource.WindowsInstaller,
            MsiProductCode = Code,
            InstallLocation = @"C:\Program Files\Foo\",
        };

        var merged = InventoryMerger.Merge([registry], [msi], []);

        var app = Assert.Single(merged);
        Assert.Equal("reg", app.Id);
        Assert.Equal(@"C:\Program Files\Foo\", app.InstallLocation);
    }

    [Fact]
    public void Msi_product_without_registry_entry_is_flagged_as_hidden()
    {
        var msi = new InstalledApp { Id = "msi", DisplayName = "Hidden", Source = AppSource.WindowsInstaller, MsiProductCode = Code };

        var app = Assert.Single(InventoryMerger.Merge([], [msi], []));

        Assert.True(app.IsNotListedByWindows);
    }

    [Fact]
    public void Same_entry_in_32_and_64_bit_views_appears_once()
    {
        InstalledApp Entry(string id) => new()
        {
            Id = id,
            DisplayName = "Bar 1.0",
            DisplayVersion = "1.0",
            Source = AppSource.Registry,
            UninstallString = @"C:\Bar\uninst.exe",
        };

        Assert.Single(InventoryMerger.Merge([Entry("a"), Entry("b")], [], []));
    }

    [Fact]
    public void RegistryLocation_shows_wow6432node_for_32_bit_view()
    {
        var location = new RegistryLocation("HKLM", true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\X");

        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\X", location.ToString());
    }
}
