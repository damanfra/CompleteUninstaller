using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;

namespace CompleteUninstaller.Core.Tests;

public class UnixPathTests
{
    [Theory]
    [InlineData("/home/ana/.config/Foo", "/home/ana/.config", true)]
    [InlineData("/home/ana/.config-old/Foo", "/home/ana/.config", false)]
    [InlineData("/home/ana/.config", "/home/ana/.config", true)]
    [InlineData("/Home/ana", "/home/ana", false)] // diferencia maiúsculas
    [InlineData("/anything", "/", true)]
    public void IsSameOrUnder(string path, string root, bool expected) =>
        Assert.Equal(expected, UnixPath.IsSameOrUnder(path, root));

    [Fact]
    public void Normalize_collapses_slashes_and_dots() =>
        Assert.Equal("/a/b/c", UnixPath.Normalize("//a/./b///c/"));

    [Theory]
    [InlineData("/home/ana/.config/x/../..", false)]
    [InlineData("/home/ana/../root/.ssh", false)]
    [InlineData("relative/path", false)]
    [InlineData("C:\\Windows", false)]
    [InlineData("/ok/path", true)]
    [InlineData("/dots/..hidden/file", true)]
    public void IsRootedLocal_rejects_parent_segments_and_relative_paths(string path, bool expected) =>
        Assert.Equal(expected, UnixPath.IsRootedLocal(path));

    [Fact]
    public void Parent_and_leaf()
    {
        Assert.Equal("/", UnixPath.GetParent("/opt"));
        Assert.Null(UnixPath.GetParent("/"));
        Assert.Equal("/opt/foo", UnixPath.GetParent("/opt/foo/bar"));
        Assert.Equal("bar", UnixPath.GetLeaf("/opt/foo/bar/"));
        Assert.Equal("/opt/foo", UnixPath.Combine("/opt", "/foo"));
    }
}

public class UnixPathGuardTests
{
    private const string Home = "/home/ana";

    private static readonly UnixProfile Ana = new("ana", Home,
    [
        "/home/ana/Documentos", "/home/ana/Downloads", "/home/ana/Imagens",
    ]);

    private static PathGuard CreateGuard(Func<string, string?>? ownerOf = null, params string[] otherApps) =>
        UnixSafetyRules.CreateGuard([Ana], ["/var/lib/CompleteUninstaller", "/home/ana/.local/share/CompleteUninstaller"],
            otherApps, ownerOf);

    [Theory]
    [InlineData("/", false)]
    [InlineData("/etc", false)]
    [InlineData("/opt", false)]
    [InlineData("/opt/foo", true)]
    [InlineData("/etc/foo.conf", true)]
    [InlineData("/etc/passwd", false)]
    [InlineData("/etc/ssh/sshd_config", false)]
    [InlineData("/etc/systemd/system", false)]
    [InlineData("/etc/systemd/system/foo.service", true)]
    [InlineData("/etc/systemd/network/10-foo.network", false)]
    [InlineData("/usr", false)]
    [InlineData("/usr/bin/foo", false)]
    [InlineData("/usr/share", false)]
    [InlineData("/usr/share/foo", true)]
    [InlineData("/usr/share/zoneinfo/Europe", false)]
    [InlineData("/usr/lib/x86_64-linux-gnu/libfoo.so", false)]
    [InlineData("/usr/lib/foo", true)]
    [InlineData("/boot/grub", false)]
    [InlineData("/proc/1", false)]
    [InlineData("/var/lib/dpkg/info/foo.list", false)]
    [InlineData("/var/lib/snapd/snaps/foo.snap", false)]
    [InlineData("/var/lib/foo", true)]
    [InlineData("/var/foo", false)] // fora das áreas permitidas
    [InlineData("/srv/foo", true)]
    [InlineData("/tmp/foo", false)]
    [InlineData("/mnt/disk/foo", false)]
    public void System_locations(string path, bool expected) =>
        Assert.Equal(expected, CreateGuard().CanRemove(path));

    [Theory]
    [InlineData("/home/ana", false)]
    [InlineData("/home/ana/.config", false)]
    [InlineData("/home/ana/.config/Foo", true)]
    [InlineData("/home/ana/.local/share/Foo", true)]
    [InlineData("/home/ana/.cache/foo", true)]
    [InlineData("/home/ana/.mozilla", true)]
    [InlineData("/home/ana/.var/app/org.foo.Bar", true)]
    [InlineData("/home/ana/snap/foo", true)]
    [InlineData("/home/ana/snap", false)]
    [InlineData("/home/ana/.ssh", false)]
    [InlineData("/home/ana/.ssh/id_rsa", false)]
    [InlineData("/home/ana/.gnupg/pubring.kbx", false)]
    [InlineData("/home/ana/.local/share/keyrings/login.keyring", false)]
    [InlineData("/home/ana/.local/share/Trash/files", false)]
    [InlineData("/home/ana/.local/share/flatpak/app/org.foo.Bar", false)]
    [InlineData("/home/ana/.local/share/CompleteUninstaller/Quarantine", false)]
    [InlineData("/home/ana/.local/share/applications", false)]
    [InlineData("/home/ana/.local/share/applications/foo.desktop", true)]
    public void Home_dot_folders(string path, bool expected) =>
        Assert.Equal(expected, CreateGuard().CanRemove(path));

    [Theory]
    [InlineData("/home/ana/Documentos")]
    [InlineData("/home/ana/Documentos/foo")]
    [InlineData("/home/ana/Downloads/foo.tar.gz")]
    [InlineData("/home/ana/Projetos")]            // pasta comum da pasta pessoal
    [InlineData("/home/ana/Projetos/foo/.config")] // dentro de pasta comum
    [InlineData("/home/ana/Foo")]
    [InlineData("/home/bob/.config/Foo")]          // outro usuário: fora da varredura
    [InlineData("/root/.config/Foo")]
    public void Never_touches_user_files(string path) =>
        Assert.False(CreateGuard().CanRemove(path));

    [Theory]
    [InlineData("/home/ana/.config/x/../..")]
    [InlineData("/opt/foo/../../etc/passwd")]
    [InlineData("/home/ana/.config/../Documentos")]
    public void Rejects_parent_traversal(string path) =>
        Assert.False(CreateGuard().CanRemove(path));

    [Fact]
    public void Never_touches_paths_owned_by_installed_packages()
    {
        var guard = CreateGuard(path => path.StartsWith("/usr/share/shared-data", StringComparison.Ordinal) ? "libshared" : null);

        Assert.False(guard.CanRemove("/usr/share/shared-data", out var reason));
        Assert.Contains("libshared", reason, StringComparison.Ordinal);
        Assert.True(guard.CanRemove("/usr/share/foo"));
    }

    [Fact]
    public void Never_touches_other_installed_apps()
    {
        var guard = CreateGuard(null, "/opt/vendor/other");

        Assert.False(guard.CanRemove("/opt/vendor"));               // contém outro app
        Assert.False(guard.CanRemove("/opt/vendor/other/plugins")); // dentro de outro app
        Assert.True(guard.CanRemove("/opt/vendor/this"));
    }

    [Fact]
    public void Ignores_meaningless_other_locations()
    {
        var guard = CreateGuard(null, "/", "/opt", "/usr");

        Assert.True(guard.CanRemove("/opt/foo"));
    }

    [Fact]
    public void Paths_are_case_sensitive()
    {
        var guard = CreateGuard();

        Assert.False(guard.CanRemove("/etc/passwd"));
        Assert.True(guard.CanRemove("/etc/Passwd"));   // outro arquivo: o sistema de arquivos diferencia maiúsculas
        Assert.False(guard.CanRemove("/OPT/Foo"));     // "/OPT" não é "/opt": fora das áreas permitidas
    }
}

public class UnixLeftoverCollectorTests
{
    private static LeftoverItem Folder(string path, Confidence confidence) =>
        new() { Kind = LeftoverKind.Folder, Target = path, Confidence = confidence, Reason = "teste" };

    [Fact]
    public void Paths_differing_only_by_case_are_different_items()
    {
        var collector = new LeftoverCollector(UnixPathRules.Instance);
        collector.Add(Folder("/home/ana/.config/Foo", Confidence.High));
        collector.Add(Folder("/home/ana/.config/foo", Confidence.High));

        Assert.Equal(2, collector.Build(CleanupLevel.Safe).Count);
    }

    [Fact]
    public void Items_inside_a_folder_are_redundant_but_siblings_with_same_prefix_are_not()
    {
        var collector = new LeftoverCollector(UnixPathRules.Instance);
        collector.Add(Folder("/opt/foo", Confidence.High));
        collector.Add(Folder("/opt/foo/data", Confidence.High));
        collector.Add(Folder("/opt/foobar", Confidence.High));

        var items = collector.Build(CleanupLevel.Safe);

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i.Target == "/opt/foo/data");
    }

    [Fact]
    public void Moderate_level_keeps_medium_items()
    {
        var collector = new LeftoverCollector(UnixPathRules.Instance);
        collector.Add(Folder("/opt/a", Confidence.Medium));
        collector.Add(Folder("/opt/b", Confidence.High));

        Assert.Equal(2, collector.Build(CleanupLevel.Moderate).Count);
        Assert.Single(collector.Build(CleanupLevel.Safe));
    }
}

public class LinuxGenericNameTests
{
    [Theory]
    [InlineData("share")]
    [InlineData("applications")]
    [InlineData("autostart")]
    [InlineData("systemd")]
    [InlineData("icons")]
    [InlineData("dbus-1")]
    [InlineData("gtk-3.0")]
    [InlineData("flatpak")]
    [InlineData("snap")]
    public void Linux_generic_names_never_identify_a_program(string folder)
    {
        // Um programa cujo nome é exatamente igual a uma pasta genérica não pode "reivindicá-la".
        var matcher = new AppNameMatcher(folder, null, extraGenericNames: UnixSafetyRules.GenericNames);

        Assert.Equal(NameMatch.None, matcher.Match(folder));
    }

    [Fact]
    public void Real_program_folders_still_match()
    {
        var matcher = new AppNameMatcher("Firefox", "Mozilla", ["firefox"], UnixSafetyRules.GenericNames);

        Assert.Equal(NameMatch.Exact, matcher.Match("firefox"));
        Assert.Equal(NameMatch.None, matcher.Match("share"));
        Assert.Equal(NameMatch.Publisher, matcher.Match("mozilla"));
    }

    [Fact]
    public void Dashed_package_names_match_their_config_folders()
    {
        var matcher = new AppNameMatcher("gnome-calculator", null, ["gnome-calculator", "calculator"], UnixSafetyRules.GenericNames);

        Assert.Equal(NameMatch.Exact, matcher.Match("gnome-calculator"));
    }
}
