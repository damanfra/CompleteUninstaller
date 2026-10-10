using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Infrastructure.Linux.Removal;

namespace CompleteUninstaller.Core.Tests;

/// <summary>O auxiliar roda como root e não confia no pedido: cada item é validado de novo.</summary>
public class HelperValidationTests
{
    private static readonly PathGuard Guard = UnixSafetyRules.CreateGuard([], ["/var/lib/CompleteUninstaller"], [], null);

    private static bool Valid(string kind, string path, string id = "0001") =>
        PrivilegedHelper.ValidateItem(new HelperItem { Id = id, Kind = kind, Path = path }, Guard, out _);

    [Theory]
    [InlineData("Folder", "/opt/foo", true)]
    [InlineData("File", "/etc/foo/foo.conf", true)]
    [InlineData("Shortcut", "/usr/share/applications/foo.desktop", true)]
    [InlineData("Service", "/etc/systemd/system/foo.service", true)]
    [InlineData("Service", "/etc/systemd/system/foo.timer", true)]
    [InlineData("Service", "/usr/lib/systemd/system/foo.service", false)]  // só /etc/systemd/system
    [InlineData("Service", "/etc/systemd/system/foo.sh", false)]
    [InlineData("Service", "/etc/systemd/system/sub/foo.service", false)]
    [InlineData("Folder", "/home/ana/.config/foo", false)]                  // pastas pessoais não passam pelo auxiliar
    [InlineData("Folder", "/root/.config/foo", false)]
    [InlineData("Folder", "/etc", false)]
    [InlineData("File", "/etc/passwd", false)]
    [InlineData("File", "/etc/shadow", false)]
    [InlineData("Folder", "/usr/bin", false)]
    [InlineData("Folder", "/boot/grub", false)]
    [InlineData("Folder", "/var/lib/dpkg/info", false)]
    [InlineData("Folder", "/var/lib/CompleteUninstaller/Quarantine", false)]
    [InlineData("Folder", "/opt/foo/../../etc", false)]
    [InlineData("Folder", "opt/foo", false)]
    [InlineData("Folder", "", false)]
    [InlineData("ScheduledTask", "/opt/foo", false)]
    [InlineData("Banana", "/opt/foo", false)]
    [InlineData("1", "/opt/foo", false)]                                   // número no lugar do nome do tipo
    [InlineData("99", "/opt/foo", false)]
    public void Items_are_validated_independently_of_the_request(string kind, string path, bool expected) =>
        Assert.Equal(expected, Valid(kind, path));

    [Theory]
    [InlineData("0001", true)]
    [InlineData("0001_2", true)]
    [InlineData("12", false)]
    [InlineData("../0001", false)]
    [InlineData("0001/../../x", false)]
    [InlineData("", false)]
    public void Item_ids(string id, bool expected) =>
        Assert.Equal(expected, HelperProtocol.IsValidItemId(id));

    [Theory]
    [InlineData("20240101-101500_Firefox", true)]
    [InlineData("a", true)]
    [InlineData("../etc", false)]
    [InlineData("a..b", false)]
    [InlineData("a/b", false)]
    [InlineData("-rf", false)]
    [InlineData("", false)]
    public void Session_ids(string id, bool expected) =>
        Assert.Equal(expected, HelperProtocol.IsValidSessionId(id));

    [Theory]
    [InlineData("items/0001/foo", "0001", true)]
    [InlineData("items/0001/.mozilla", "0001", true)]
    [InlineData("items/0002/foo", "0001", false)]
    [InlineData("items/0001/..", "0001", false)]
    [InlineData("items/0001/.", "0001", false)]
    [InlineData("items/0001/a/b", "0001", false)]
    [InlineData("../items/0001/foo", "0001", false)]
    [InlineData("/items/0001/foo", "0001", false)]
    public void Stored_paths(string stored, string id, bool expected) =>
        Assert.Equal(expected, HelperProtocol.IsValidStored(stored, id));

    [Theory]
    [InlineData("foo.service", true)]
    [InlineData("foo@bar.service", true)]
    [InlineData("foo.timer", true)]
    [InlineData("foo.service; rm -rf /", false)]
    [InlineData("foo bar.service", false)]
    [InlineData("-foo.service", false)] // começaria como opção do systemctl
    [InlineData("foo.socket", false)]
    public void Unit_names(string name, bool expected) =>
        Assert.Equal(expected, HelperProtocol.IsValidUnitName(name));
}
