using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.Core.Tests;

public class CommandLineParserTests
{
    private static bool FakeExists(string path) =>
        path.Equals(@"C:\Program Files\Foo App\uninst.exe", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Quoted_executable()
    {
        var parsed = CommandLineParser.Parse("\"C:\\Program Files\\Foo\\unins000.exe\" /SILENT", _ => false);

        Assert.NotNull(parsed);
        Assert.Equal(@"C:\Program Files\Foo\unins000.exe", parsed.FileName);
        Assert.Equal("/SILENT", parsed.Arguments);
    }

    [Fact]
    public void Unquoted_path_with_spaces_uses_file_existence()
    {
        var parsed = CommandLineParser.Parse(@"C:\Program Files\Foo App\uninst.exe /S", FakeExists);

        Assert.NotNull(parsed);
        Assert.Equal(@"C:\Program Files\Foo App\uninst.exe", parsed.FileName);
        Assert.Equal("/S", parsed.Arguments);
    }

    [Fact]
    public void Unquoted_path_falls_back_to_first_exe_token()
    {
        var parsed = CommandLineParser.Parse(@"C:\Program Files\Gone\setup.exe --uninstall", _ => false);

        Assert.NotNull(parsed);
        Assert.Equal(@"C:\Program Files\Gone\setup.exe", parsed.FileName);
        Assert.Equal("--uninstall", parsed.Arguments);
    }

    [Theory]
    [InlineData("MsiExec.exe /I{23170F69-40C1-2702-2408-000001000000}", "{23170F69-40C1-2702-2408-000001000000}")]
    [InlineData("MsiExec.exe /X{23170f69-40c1-2702-2408-000001000000}", "{23170F69-40C1-2702-2408-000001000000}")]
    [InlineData("\"C:\\Windows\\System32\\msiexec.exe\" /x {11111111-2222-3333-4444-555555555555} /qn", "{11111111-2222-3333-4444-555555555555}")]
    [InlineData("\"C:\\Program Files\\Foo\\uninst.exe\" /S", null)]
    [InlineData(null, null)]
    public void Extracts_msi_product_code(string? command, string? expected) =>
        Assert.Equal(expected, CommandLineParser.ExtractMsiProductCode(command));

    [Theory]
    [InlineData(@"C:\App\app.exe,0", @"C:\App\app.exe")]
    [InlineData("\"C:\\App Dir\\app.exe\",1", @"C:\App Dir\app.exe")]
    [InlineData(@"C:\App\app.ico", @"C:\App\app.ico")]
    public void Extracts_icon_path(string displayIcon, string expected) =>
        Assert.Equal(expected, CommandLineParser.ExtractIconPath(displayIcon));
}

public class UninstallCommandBuilderTests
{
    private static InstalledApp RegistryApp(string? uninstall, string? quiet = null, bool msi = false, string? code = null) => new()
    {
        Id = "test",
        DisplayName = "Test",
        Source = AppSource.Registry,
        UninstallString = uninstall,
        QuietUninstallString = quiet,
        IsWindowsInstaller = msi,
        MsiProductCode = code,
    };

    [Fact]
    public void Msi_products_use_msiexec_uninstall_instead_of_maintenance_mode()
    {
        var app = RegistryApp("MsiExec.exe /I{11111111-2222-3333-4444-555555555555}", msi: true,
            code: "{11111111-2222-3333-4444-555555555555}");

        var plan = UninstallCommandBuilder.Build(app, preferQuiet: false, _ => false);

        Assert.NotNull(plan);
        Assert.Equal(UninstallMethod.WindowsInstaller, plan.Method);
        Assert.Equal("msiexec.exe /x {11111111-2222-3333-4444-555555555555}", plan.CommandLine);
    }

    [Fact]
    public void Quiet_msi_suppresses_reboot()
    {
        var app = RegistryApp(null, msi: true, code: "{11111111-2222-3333-4444-555555555555}");

        var plan = UninstallCommandBuilder.Build(app, preferQuiet: true, _ => false);

        Assert.NotNull(plan);
        Assert.Contains("/qb", plan.CommandLine);
        Assert.Contains("REBOOT=ReallySuppress", plan.CommandLine);
    }

    [Fact]
    public void Prefers_quiet_string_when_requested()
    {
        var app = RegistryApp("\"C:\\Foo\\unins000.exe\"", "\"C:\\Foo\\unins000.exe\" /VERYSILENT");

        var normal = UninstallCommandBuilder.Build(app, preferQuiet: false, _ => false);
        var quiet = UninstallCommandBuilder.Build(app, preferQuiet: true, _ => false);

        Assert.Equal("\"C:\\Foo\\unins000.exe\"", normal!.CommandLine);
        Assert.Equal("\"C:\\Foo\\unins000.exe\" /VERYSILENT", quiet!.CommandLine);
        Assert.True(quiet.IsQuiet);
        Assert.Equal(@"C:\Foo", quiet.WorkingDirectory);
    }

    [Fact]
    public void Store_apps_use_package_removal()
    {
        var app = new InstalledApp
        {
            Id = "appx",
            DisplayName = "Calc",
            Source = AppSource.Store,
            PackageFullName = "Microsoft.WindowsCalculator_11.0.0.0_x64__8wekyb3d8bbwe",
        };

        var plan = UninstallCommandBuilder.Build(app, preferQuiet: false, _ => false);

        Assert.NotNull(plan);
        Assert.Equal(UninstallMethod.StorePackage, plan.Method);
        Assert.Equal(app.PackageFullName, plan.CommandLine);
    }

    [Fact]
    public void Returns_null_without_uninstaller()
    {
        Assert.Null(UninstallCommandBuilder.Build(RegistryApp(null), preferQuiet: false, _ => false));
    }
}
