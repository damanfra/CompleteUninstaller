using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.Core.Tests;

public class DpkgParserTests
{
    private const string Sample =
        "ii \tfirefox\tamd64\t120.0+build1-0ubuntu1\t250000\tUbuntu Mozilla Team <ubuntu-mozillateam@lists.ubuntu.com>\tweb\toptional\t\tSafe and easy web browser\thttp://www.mozilla.com/\n" +
        "ii \tlibc6\tamd64\t2.39-0ubuntu8\t13000\tUbuntu Developers <ubuntu-devel@lists.ubuntu.com>\tlibs\trequired\t\tGNU C Library\thttps://www.gnu.org/\n" +
        "ii \tbash\tamd64\t5.2.21\t7000\tUbuntu Developers <ubuntu-devel@lists.ubuntu.com>\tshells\trequired\tyes\tGNU Bourne Again SHell\t\n" +
        "ii \thtop\tamd64\t3.3.0-4\t400\tDaniel Lange <dlange@debian.org>\tuniverse/utils\toptional\t\tinteractive process viewer\t\n" +
        "ii \tlibfoo1\tamd64\t1.0\t100\tSomeone <a@b.c>\tlibs\toptional\t\tFoo library\t\n" +
        "ii \ttzdata\tall\t2024a\t3000\tDebian <x@y.z>\tlocalization\tcritical\t\ttime zone data\t\n" +
        "rc \toldtool\tamd64\t1.0\t90\tSomeone <a@b.c>\tutils\toptional\t\told tool\t\n" +
        "un \tghost\t<none>\t<none>\t\t\t\t\t\t\t\n";

    private static readonly HashSet<string> Manual = ["firefox", "bash", "htop"];

    [Fact]
    public void Parses_installed_packages_and_residual_config()
    {
        var result = PackageParsers.ParseDpkg(Sample, Manual);

        Assert.Equal(["firefox:amd64", "libc6:amd64", "bash:amd64", "htop:amd64", "libfoo1:amd64", "tzdata"],
            result.Installed.Select(a => a.PackageName!).ToArray());
        Assert.Equal(["oldtool"], result.ResidualConfig);
    }

    [Fact]
    public void Firefox_has_clean_fields()
    {
        var firefox = PackageParsers.ParseDpkg(Sample, Manual).Installed.First(a => a.DisplayName == "firefox");

        Assert.Equal("firefox:amd64", firefox.PackageName);
        Assert.Equal("Ubuntu Mozilla Team", firefox.Publisher);
        Assert.Equal(250000L * 1024, firefox.EstimatedSizeBytes);
        Assert.Equal(AppSource.Dpkg, firefox.Source);
        Assert.False(firefox.IsLibrary);
        Assert.False(firefox.IsSystemComponent);
    }

    [Fact]
    public void Essential_and_required_packages_cannot_be_removed()
    {
        var apps = PackageParsers.ParseDpkg(Sample, Manual).Installed.ToDictionary(a => a.DisplayName);

        Assert.True(apps["bash"].Flags.HasFlag(AppFlags.NoRemove));
        Assert.True(apps["libc6"].Flags.HasFlag(AppFlags.NoRemove));
        Assert.False(apps["htop"].Flags.HasFlag(AppFlags.NoRemove));
    }

    [Fact]
    public void Libraries_and_auto_installed_packages_are_flagged_so_the_list_stays_short()
    {
        var apps = PackageParsers.ParseDpkg(Sample, Manual).Installed.ToDictionary(a => a.DisplayName);

        Assert.True(apps["libfoo1"].IsLibrary);   // seção libs
        Assert.True(apps["tzdata"].IsLibrary);    // instalado automaticamente
        Assert.False(apps["htop"].IsLibrary);     // manual, seção "universe/utils"
    }

    [Fact]
    public void Without_manual_list_nothing_is_flagged_as_auto_installed()
    {
        var apps = PackageParsers.ParseDpkg(Sample).Installed.ToDictionary(a => a.DisplayName);

        Assert.False(apps["tzdata"].IsLibrary);
        Assert.True(apps["libfoo1"].IsLibrary);
    }

    [Fact]
    public void Garbage_lines_are_ignored()
    {
        var result = PackageParsers.ParseDpkg("dpkg-query: aviso qualquer\n\nii \tx\n");

        Assert.Empty(result.Installed);
    }
}

public class RpmFlatpakSnapParserTests
{
    [Fact]
    public void Rpm_sample()
    {
        const string output =
            "firefox\t120.0-1.fc39\tx86_64\tFedora Project\t263000000\t1700000000\tMozilla Firefox Web browser\thttps://www.mozilla.org/\n" +
            "gpg-pubkey\t105ef944-65ca83d1\t(none)\t(none)\t0\t1700000000\tgpg(Fedora)\t(none)\n" +
            "glibc\t2.38-7.fc39\tx86_64\tFedora Project\t6000000\t1700000000\tThe GNU libc libraries\thttp://www.gnu.org/software/glibc/\n" +
            "libfoo\t1.0-1.fc39\tx86_64\t(none)\t1000\t1700000000\tFoo\t(none)\n" +
            "kernel-core\t6.5.6-300.fc39\tx86_64\tFedora Project\t90000000\t1700000000\tKernel\thttps://www.kernel.org/\n";

        var apps = PackageParsers.ParseRpm(output).ToDictionary(a => a.DisplayName);

        Assert.DoesNotContain("gpg-pubkey", apps.Keys);
        Assert.Equal("Fedora Project", apps["firefox"].Publisher);
        Assert.NotNull(apps["firefox"].InstallDate);
        Assert.False(apps["firefox"].IsLibrary);
        Assert.True(apps["glibc"].Flags.HasFlag(AppFlags.NoRemove));
        Assert.True(apps["kernel-core"].Flags.HasFlag(AppFlags.NoRemove));
        Assert.True(apps["libfoo"].IsLibrary);
        Assert.Null(apps["libfoo"].Publisher);
    }

    [Fact]
    public void Flatpak_sample()
    {
        const string output =
            "org.mozilla.firefox\tFirefox\t120.0\tstable\tuser\tflathub\t310.5 MB\n" +
            "org.gnome.Calculator\tCalculator\t\tstable\tsystem\tflathub\t12.3 MB\n";

        var apps = PackageParsers.ParseFlatpak(output, "/home/ana", runtimes: false);

        Assert.Equal(2, apps.Count);
        Assert.Equal("/home/ana/.local/share/flatpak/app/org.mozilla.firefox", apps[0].InstallLocation);
        Assert.Equal("user", apps[0].PackageScope);
        Assert.Equal("stable", apps[0].PackageBranch);
        Assert.Contains("firefox", apps[0].ExtraNames);
        Assert.Equal("/var/lib/flatpak/app/org.gnome.Calculator", apps[1].InstallLocation);
        Assert.Equal("stable", apps[1].DisplayVersion); // sem versão: usa o ramo
        Assert.Equal((long)(310.5 * 1024 * 1024), apps[0].EstimatedSizeBytes);
    }

    [Fact]
    public void Flatpak_runtimes_are_libraries()
    {
        var apps = PackageParsers.ParseFlatpak("org.freedesktop.Platform\tFreedesktop Platform\t23.08\t23.08\tsystem\tflathub\t500 MB\n", "/home/ana", runtimes: true);

        Assert.True(Assert.Single(apps).IsLibrary);
    }

    [Fact]
    public void Snap_sample_hides_base_and_core_snaps()
    {
        const string output =
            "Name               Version          Rev    Tracking         Publisher   Notes\n" +
            "bare               1.0              5      latest/stable    canonical** base\n" +
            "core22             20240111         1380   latest/stable    canonical** base\n" +
            "firefox            120.0-1          3626   latest/stable/…  mozilla**   -\n" +
            "gnome-42-2204      0+git.510a2d4    176    latest/stable/…  canonical** -\n" +
            "snapd              2.61.1           20290  latest/stable    canonical** snapd\n" +
            "vlc                3.0.20           3777   latest/stable    videolan**  -\n";

        var apps = PackageParsers.ParseSnap(output).ToDictionary(a => a.DisplayName);

        Assert.Equal(6, apps.Count);
        Assert.True(apps["core22"].IsSystemComponent);
        Assert.True(apps["snapd"].IsSystemComponent);
        Assert.True(apps["bare"].IsLibrary);
        Assert.True(apps["gnome-42-2204"].IsLibrary);
        Assert.False(apps["firefox"].IsLibrary);
        Assert.Equal("mozilla", apps["firefox"].Publisher);
        Assert.Equal("3.0.20", apps["vlc"].DisplayVersion);
    }

    [Theory]
    [InlineData("1.2 GB", 1288490188L)]
    [InlineData("340.5 MB", 357040128L)]
    [InlineData("12 kB", 12288L)]
    [InlineData("512 bytes", 512L)]
    [InlineData("1,5 MB", 1572864L)]
    public void Human_sizes(string text, long expected) =>
        Assert.Equal(expected, PackageParsers.ParseHumanSize(text));

    [Theory]
    [InlineData("")]
    [InlineData("n/a")]
    [InlineData("12 parsecs")]
    public void Human_size_garbage_is_null(string text) =>
        Assert.Null(PackageParsers.ParseHumanSize(text));
}

public class ToolOutputParserTests
{
    [Fact]
    public void Apt_simulation_lists_removed_packages()
    {
        const string output =
            "Reading package lists...\nThe following packages will be REMOVED:\n  firefox foo-plugin\n0 upgraded, 0 newly installed, 2 to remove and 0 not upgraded.\n" +
            "Remv foo-plugin:amd64 [1.0]\nRemv firefox [120.0]\nConf something\n";

        Assert.Equal(["foo-plugin:amd64", "firefox"], ToolOutputParsers.ParseAptSimulation(output));
    }

    [Fact]
    public void Dnf4_simulation_lists_dependents()
    {
        const string output =
            "Dependencies resolved.\n================================\n Package   Arch   Version   Repository   Size\n================================\n" +
            "Removing:\n firefox      x86_64   120.0-1.fc39   @updates   250 M\n" +
            "Removing dependent packages:\n foo-plugin   x86_64   1.0-1.fc39   @updates   1.0 M\n" +
            "\nTransaction Summary\n================================\nRemove  2 Packages\n";

        Assert.Equal(["firefox", "foo-plugin"], ToolOutputParsers.ParseDnfSimulation(output));
    }

    [Fact]
    public void Dnf_simulation_with_wrapped_rows_reads_only_the_names()
    {
        const string output =
            "Removing:\n very-long-package-name-that-wraps\n                        x86_64   1.0-1   @repo   10 M\n\nTransaction Summary\n";

        Assert.Equal(["very-long-package-name-that-wraps"], ToolOutputParsers.ParseDnfSimulation(output));
    }

    [Fact]
    public void Dependents_exclude_the_target_itself()
    {
        var app = new InstalledApp { Id = "dpkg:firefox:amd64", DisplayName = "firefox", Source = AppSource.Dpkg, PackageName = "firefox:amd64" };

        var others = LinuxUninstallCommands.OtherPackagesInSimulation(app, "Remv firefox:amd64 [1]\nRemv foo-plugin [1]\n");

        Assert.Equal(["foo-plugin"], others);
    }

    [Fact]
    public void Dpkg_search_maps_paths_to_owners_and_ignores_noise()
    {
        const string output =
            "libc6:amd64, libc-bin: /usr/lib\nfoo: /opt/foo\ndpkg-query: no path found matching pattern /opt/gone\ndiversion by dash from: /bin/sh\n";

        var map = ToolOutputParsers.ParseDpkgSearch(output);

        Assert.Equal(["libc6", "libc-bin"], map["/usr/lib"]);
        Assert.Equal(["foo"], map["/opt/foo"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void File_list_drops_root_markers_and_duplicates()
    {
        const string output = "/.\n/usr\n/usr/share/applications\n/usr/share/applications/foo.desktop\n/usr\npackage diverts others to: /x\n";

        Assert.Equal(["/usr", "/usr/share/applications", "/usr/share/applications/foo.desktop"],
            ToolOutputParsers.ParseFileList(output));
    }

    [Fact]
    public void Conffiles_are_extracted()
    {
        const string output = " /etc/foo/foo.conf 0123456789abcdef0123456789abcdef\n /etc/init.d/foo abcdef0123456789abcdef0123456789 obsolete\n";

        Assert.Equal(["/etc/foo/foo.conf", "/etc/init.d/foo"], ToolOutputParsers.ParseConffiles(output));
    }

    [Fact]
    public void Rpm_owners_zip_with_queried_paths()
    {
        var paths = new[] { "/usr/bin/a", "/opt/gone", "/etc/b" };
        var output = "bash\nfile /opt/gone is not owned by any package\nfilesystem\n";

        var map = ToolOutputParsers.ParseRpmOwners(paths, output);

        Assert.Equal("bash", map["/usr/bin/a"]);
        Assert.False(map.ContainsKey("/opt/gone"));
        Assert.Equal("filesystem", map["/etc/b"]);
    }

    [Fact]
    public void Rpm_owners_with_mismatched_output_claim_nothing() =>
        Assert.Empty(ToolOutputParsers.ParseRpmOwners(["/a", "/b"], "bash\n"));
}

public class DesktopEntryTests
{
    private const string Sample =
        "[Desktop Entry]\nName=Firefox\nName[pt_BR]=Firefox em português\nExec=env GDK_BACKEND=x11 \"/usr/lib/firefox/firefox\" %u\nIcon=firefox\nType=Application\nNoDisplay=false\n\n[Desktop Action new-window]\nName=Nova janela\nExec=firefox --new-window\n";

    [Fact]
    public void Reads_main_section_only()
    {
        var entry = DesktopEntry.Parse("/usr/share/applications/firefox.desktop", Sample);

        Assert.NotNull(entry);
        Assert.Equal("Firefox", entry.Name);
        Assert.Equal("firefox", entry.Icon);
        Assert.Equal("/usr/lib/firefox/firefox", entry.ExecProgram);
        Assert.Equal("firefox", entry.Stem);
        Assert.False(entry.Hidden);
    }

    [Theory]
    [InlineData("firefox %u", "firefox")]
    [InlineData("/usr/bin/flatpak run --branch=stable org.foo.Bar", "/usr/bin/flatpak")]
    [InlineData("env FOO=1 BAR=2 /opt/foo/bin/foo", "/opt/foo/bin/foo")]
    [InlineData("\"/opt/My App/run\" --x", "/opt/My App/run")]
    [InlineData("", null)]
    public void Exec_program(string exec, string? expected) =>
        Assert.Equal(expected, DesktopEntry.ExtractProgram(exec));

    [Fact]
    public void Entry_without_name_is_rejected() =>
        Assert.Null(DesktopEntry.Parse("/x.desktop", "[Desktop Entry]\nExec=foo\n"));

    [Fact]
    public void Hidden_entries_are_marked() =>
        Assert.True(DesktopEntry.Parse("/x.desktop", "[Desktop Entry]\nName=X\nNoDisplay=true\n")!.Hidden);
}

public class LinuxUninstallCommandTests
{
    private static InstalledApp App(AppSource source, string name, string? scope = null, string? branch = null, AppFlags flags = AppFlags.None) =>
        new() { Id = "x", DisplayName = name, Source = source, PackageName = name, PackageScope = scope, PackageBranch = branch, Flags = flags };

    [Fact]
    public void Apt_removes_without_purge_and_simulates_first()
    {
        var plan = LinuxUninstallCommands.Build(App(AppSource.Dpkg, "firefox:amd64"));

        Assert.Equal("apt-get", plan.Remove!.FileName);
        Assert.Equal(["remove", "-y", "firefox:amd64"], plan.Remove.Arguments);
        Assert.True(plan.Remove.NeedsRoot);
        Assert.Equal(["-s", "remove", "firefox:amd64"], plan.Simulate!.Arguments);
        Assert.DoesNotContain(plan.Remove.Arguments, a => a.Contains("purge", StringComparison.Ordinal));
    }

    [Fact]
    public void Flatpak_keeps_user_data_and_uses_the_right_installation()
    {
        var user = LinuxUninstallCommands.Build(App(AppSource.Flatpak, "org.foo.Bar", "user", "stable")).Remove!;
        var system = LinuxUninstallCommands.Build(App(AppSource.Flatpak, "org.foo.Bar", "system", "stable")).Remove!;

        Assert.Contains("--user", user.Arguments);
        Assert.Contains("--system", system.Arguments);
        Assert.DoesNotContain("--delete-data", user.Arguments);
        Assert.Contains("org.foo.Bar//stable", user.Arguments);
        Assert.False(user.NeedsRoot);
    }

    [Fact]
    public void Snap_never_purges()
    {
        var command = LinuxUninstallCommands.Build(App(AppSource.Snap, "vlc")).Remove!;

        Assert.Equal(["remove", "vlc"], command.Arguments);
    }

    [Fact]
    public void Dnf_remove_and_simulation()
    {
        var plan = LinuxUninstallCommands.Build(App(AppSource.Rpm, "firefox"));

        Assert.Equal(["remove", "-y", "firefox"], plan.Remove!.Arguments);
        Assert.Contains("--assumeno", plan.Simulate!.Arguments);
    }

    [Theory]
    [InlineData(AppSource.Dpkg, "--force-all")]
    [InlineData(AppSource.Dpkg, "-rf")]
    [InlineData(AppSource.Dpkg, "foo bar")]
    [InlineData(AppSource.Dpkg, "foo;rm")]
    [InlineData(AppSource.Rpm, "--nodeps")]
    [InlineData(AppSource.Snap, "--purge")]
    [InlineData(AppSource.Flatpak, "-y")]
    [InlineData(AppSource.Dpkg, "")]
    public void Suspicious_names_never_become_commands(AppSource source, string name)
    {
        var plan = LinuxUninstallCommands.Build(App(source, name, "user", "stable"));

        Assert.Null(plan.Remove);
        Assert.NotNull(plan.Error);
    }

    [Fact]
    public void Essential_packages_are_refused()
    {
        var plan = LinuxUninstallCommands.Build(App(AppSource.Dpkg, "bash", flags: AppFlags.NoRemove));

        Assert.Null(plan.Remove);
        Assert.Contains("essencial", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Flatpak_branch_is_validated()
    {
        var plan = LinuxUninstallCommands.Build(App(AppSource.Flatpak, "org.foo.Bar", "user", "--all"));

        Assert.Null(plan.Remove);
    }
}
