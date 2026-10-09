using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.Core.Tests;

public class AppClassifierTests
{
    private static InstalledApp Registry(string name, string? publisher) =>
        new() { Id = name, DisplayName = name, Publisher = publisher, Source = AppSource.Registry };

    private static InstalledApp Store(string name, string publisher, string family, AppFlags flags = AppFlags.None) =>
        new() { Id = name, DisplayName = name, Publisher = publisher, Source = AppSource.Store, PackageFamilyName = family, Flags = flags };

    [Theory]
    [InlineData("Microsoft Corporation", true)]
    [InlineData("Microsoft Corp.", true)]
    [InlineData("Microsoft Studios", true)]
    [InlineData("Mozilla", false)]
    [InlineData(null, false)]
    public void IsMicrosoft_by_publisher(string? publisher, bool expected) =>
        Assert.Equal(expected, AppClassifier.IsMicrosoft(Registry("Qualquer", publisher)));

    [Fact]
    public void IsMicrosoft_by_store_package_family() =>
        Assert.True(AppClassifier.IsMicrosoft(Store("Calculadora", "", "Microsoft.WindowsCalculator_8wekyb3d8bbwe")));

    [Theory]
    [InlineData("Microsoft Edge", true)]
    [InlineData("Microsoft Edge WebView2 Runtime", true)]
    [InlineData("Microsoft OneDrive", true)]
    [InlineData("Windows PC Health Check", true)]
    [InlineData("Microsoft Update Health Tools", true)]
    [InlineData("Microsoft Visual Studio Code (User)", false)]
    [InlineData("Microsoft 365 Apps para Grandes Empresas - pt-br", false)]
    public void IsWindowsComponent_for_registry_apps(string name, bool expected) =>
        Assert.Equal(expected, AppClassifier.IsWindowsComponent(Registry(name, "Microsoft Corporation")));

    [Fact]
    public void Windows_named_app_from_another_publisher_is_not_windows() =>
        Assert.False(AppClassifier.IsWindowsComponent(Registry("Windows Firewall Control", "BiniSoft.org")));

    [Fact]
    public void Provisioned_microsoft_store_app_is_windows()
    {
        var photos = Store("Fotos", "Microsoft Corporation", "Microsoft.Windows.Photos_8wekyb3d8bbwe", AppFlags.Provisioned);
        var userApp = Store("WhatsApp", "WhatsApp Inc.", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", AppFlags.Provisioned);
        var todo = Store("Microsoft To Do", "Microsoft Corporation", "Microsoft.Todos_8wekyb3d8bbwe");

        Assert.True(AppClassifier.IsWindowsComponent(photos));
        Assert.False(AppClassifier.IsWindowsComponent(userApp));
        Assert.False(AppClassifier.IsWindowsComponent(todo));
    }
}
