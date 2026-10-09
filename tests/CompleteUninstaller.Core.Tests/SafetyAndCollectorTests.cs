using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;

namespace CompleteUninstaller.Core.Tests;

public class WinPathTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Foo", @"C:\Program Files", true)]
    [InlineData(@"C:\Program Files (x86)\Foo", @"C:\Program Files", false)]
    [InlineData(@"c:\program files\", @"C:\Program Files", true)]
    [InlineData(@"C:\Foo", @"C:\", true)]
    public void IsSameOrUnder(string path, string root, bool expected) =>
        Assert.Equal(expected, WinPath.IsSameOrUnder(path, root));

    [Fact]
    public void Parent_and_leaf()
    {
        Assert.Equal(@"C:\", WinPath.GetParent(@"C:\Foo"));
        Assert.Null(WinPath.GetParent(@"C:\"));
        Assert.Equal(@"C:\Program Files", WinPath.GetParent(@"C:\Program Files\Foo\"));
        Assert.Equal("Foo", WinPath.GetLeaf(@"C:\Program Files\Foo\"));
    }

    [Fact]
    public void Normalize_cleans_quotes_slashes_and_trailing_separator() =>
        Assert.Equal(@"C:\A\B", WinPath.Normalize("\"C:/A//B/\""));
}

public class PathGuardTests
{
    private static PathGuard CreateGuard(params string[] otherApps) => new(
        protectedExact: [@"C:\Program Files", @"C:\ProgramData", @"C:\Users\ana\AppData\Roaming",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs"],
        protectedTrees: [@"C:\Windows", @"C:\ProgramData\Microsoft",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Administrative Tools"],
        allowedTrees: [@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs"],
        otherAppLocations: otherApps);

    [Theory]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\Foo", true)]
    [InlineData(@"C:\Windows\System32\foo.dll", false)]
    [InlineData(@"C:\ProgramData\Microsoft\Crypto", false)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs", false)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Foo", true)]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Administrative Tools\x.lnk", false)]
    [InlineData(@"relative\path", false)]
    [InlineData(@"\\server\share\foo", false)]
    public void System_locations(string path, bool expected) =>
        Assert.Equal(expected, CreateGuard().CanRemove(path));

    [Fact]
    public void Never_touches_other_installed_apps()
    {
        var guard = CreateGuard(@"C:\Program Files\Vendor\OtherApp");

        Assert.False(guard.CanRemove(@"C:\Program Files\Vendor"));                    // contém outro app
        Assert.False(guard.CanRemove(@"C:\Program Files\Vendor\OtherApp\plugins"));   // dentro de outro app
        Assert.True(guard.CanRemove(@"C:\Program Files\Vendor\ThisApp"));
    }

    [Fact]
    public void Ignores_meaningless_other_locations()
    {
        // Instaladores mal-comportados registram "C:\" ou "C:\Program Files" como pasta de instalação.
        var guard = CreateGuard(@"C:\", @"C:\Program Files");

        Assert.True(guard.CanRemove(@"C:\Program Files\Foo"));
    }
}

public class LeftoverCollectorTests
{
    private static LeftoverItem Folder(string path, Confidence confidence) =>
        new() { Kind = LeftoverKind.Folder, Target = path, Confidence = confidence, Reason = "teste" };

    [Fact]
    public void Duplicates_keep_highest_confidence()
    {
        var collector = new LeftoverCollector();
        collector.Add(Folder(@"C:\Program Files\Foo", Confidence.Medium));
        collector.Add(Folder(@"c:\program files\foo\", Confidence.High));

        var item = Assert.Single(collector.Build(CleanupLevel.Moderate));
        Assert.Equal(Confidence.High, item.Confidence);
    }

    [Fact]
    public void Items_inside_a_folder_of_equal_or_higher_confidence_are_redundant()
    {
        var collector = new LeftoverCollector();
        collector.Add(Folder(@"C:\Program Files\Foo", Confidence.High));
        collector.Add(Folder(@"C:\Program Files\Foo\Data", Confidence.High));
        collector.Add(Folder(@"C:\Program Files\FooBar", Confidence.High));

        var items = collector.Build(CleanupLevel.Safe);

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i.Target.EndsWith("Data", StringComparison.Ordinal));
    }

    [Fact]
    public void High_item_inside_medium_folder_is_kept()
    {
        var collector = new LeftoverCollector();
        collector.Add(Folder(@"C:\Users\ana\AppData\Roaming\Vendor", Confidence.Medium));
        collector.Add(Folder(@"C:\Users\ana\AppData\Roaming\Vendor\Foo", Confidence.High));

        Assert.Equal(2, collector.Build(CleanupLevel.Moderate).Count);
    }

    [Fact]
    public void Safe_level_drops_medium_items()
    {
        var collector = new LeftoverCollector();
        collector.Add(Folder(@"C:\A", Confidence.Medium));
        collector.Add(Folder(@"C:\B", Confidence.High));

        var item = Assert.Single(collector.Build(CleanupLevel.Safe));
        Assert.Equal(@"C:\B", item.Target);
    }
}
