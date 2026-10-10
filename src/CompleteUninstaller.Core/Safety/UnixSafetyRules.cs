using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Core.Safety;

/// <summary>Um usuário local: pasta pessoal e pastas do usuário (Documentos, Downloads...).</summary>
public sealed record UnixProfile(string UserName, string Home, IReadOnlyList<string> PersonalFolders);

/// <summary>
/// Regras de proteção para o Linux. Diferente do Windows, a lista de sobras permitidas é uma lista
/// de áreas conhecidas (pasta pessoal e alguns locais do sistema): o que estiver fora dela é vetado.
/// </summary>
public static class UnixSafetyRules
{
    /// <summary>Únicos lugares do sistema onde uma sobra pode ser apontada (além da pasta pessoal).</summary>
    public static readonly string[] AllowedSystemRoots =
    [
        "/opt", "/etc", "/srv", "/usr/share", "/usr/lib", "/usr/libexec", "/usr/local",
        "/var/lib", "/var/cache", "/var/log", "/var/snap",
    ];

    /// <summary>Nomes de pasta que nunca identificam um programa no Linux (somam-se aos genéricos do Windows).</summary>
    public static readonly string[] GenericNames =
    [
        "share", "lib", "lib32", "lib64", "libexec", "applications", "autostart", "systemd", "icons", "mime",
        "dbus-1", "dbus", "gtk-2.0", "gtk-3.0", "gtk-4.0", "flatpak", "snap", "snapd", "opt", "etc", "var",
        "pixmaps", "fontconfig", "dconf", "pulse", "pipewire", "cups", "gnome", "kde", "xdg", "glib-2.0",
        "python", "python3", "perl", "ruby", "node", "npm", "pki", "ssl", "ca-certificates", "locale", "man",
        "info", "doc", "dist-packages", "site-packages", "keyrings", "trash", "gvfs", "tracker", "tracker3",
        "thumbnails", "bash-completion", "zsh", "sudo", "apt", "dpkg", "rpm", "dnf", "yum", "sbin", "init.d",
        "cron.d", "udev", "modprobe.d", "sysctl.d", "tmpfiles.d", "menus", "backgrounds", "themes", "sounds",
        "wallpapers", "xorg", "x11", "wayland", "polkit-1", "pam.d", "security", "local", "cache", "state",
        "kernel", "modules", "firmware", "alternatives", "default", "ssh", "gnupg", "gpg", "systemd-user",
    ];

    private static readonly string[] ProtectedSystemExact =
    [
        "/etc", "/opt", "/srv", "/home", "/root", "/mnt", "/media", "/var", "/var/lib", "/var/cache", "/var/log",
        "/var/snap", "/usr", "/usr/share", "/usr/lib", "/usr/libexec", "/usr/local", "/usr/local/share",
        "/usr/local/lib", "/usr/local/bin", "/usr/local/etc", "/usr/share/applications", "/usr/share/icons",
        "/usr/share/doc", "/usr/share/man", "/usr/share/pixmaps", "/usr/share/mime", "/usr/share/fonts",
        "/usr/local/share/applications", "/etc/xdg", "/etc/xdg/autostart", "/etc/systemd/system", "/etc/init.d",
        "/etc/cron.d", "/etc/default", "/etc/profile.d", "/etc/skel",
    ];

    private static readonly string[] ProtectedSystemTrees =
    [
        "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32", "/boot", "/proc", "/sys", "/dev", "/run", "/tmp",
        "/lost+found", "/usr/bin", "/usr/sbin", "/usr/include", "/usr/games", "/usr/src",
        "/usr/lib/modules", "/usr/lib/firmware", "/usr/lib/systemd", "/usr/lib/x86_64-linux-gnu",
        "/usr/lib/aarch64-linux-gnu", "/usr/lib/dpkg", "/usr/lib/apt", "/usr/lib/rpm", "/usr/lib/sysimage",
        "/usr/lib/python3", "/usr/lib64", "/usr/share/zoneinfo", "/usr/share/locale", "/usr/share/glib-2.0",
        "/usr/share/dbus-1", "/usr/share/polkit-1", "/usr/share/X11", "/usr/share/ca-certificates",
        "/var/lib/dpkg", "/var/lib/apt", "/var/lib/rpm", "/var/lib/dnf", "/var/lib/yum", "/var/lib/snapd",
        "/var/lib/flatpak", "/var/lib/systemd", "/var/lib/polkit-1", "/var/lib/AccountsService", "/var/lib/gdm3",
        "/var/lib/NetworkManager", "/var/lib/ucf", "/var/lib/PackageKit", "/var/lib/selinux", "/var/lib/alternatives",
        "/var/cache/apt", "/var/cache/dnf", "/var/cache/PackageKit", "/var/log/journal", "/var/log/apt",
        "/var/log/dpkg.log", "/etc/passwd", "/etc/shadow", "/etc/group", "/etc/gshadow", "/etc/sudoers",
        "/etc/sudoers.d", "/etc/fstab", "/etc/ssh", "/etc/ssl", "/etc/pam.d", "/etc/security", "/etc/alternatives",
        "/etc/apt", "/etc/dpkg", "/etc/yum.repos.d", "/etc/dnf", "/etc/rpm", "/etc/X11", "/etc/udev",
        "/etc/systemd", "/etc/NetworkManager", "/etc/polkit-1", "/etc/selinux", "/etc/ca-certificates",
        "/etc/grub.d", "/etc/default/grub", "/etc/hosts", "/etc/resolv.conf", "/etc/machine-id",
    ];

    private static readonly string[] AllowedInsideProtected =
    [
        "/usr/share", "/usr/lib", "/usr/libexec", "/usr/local", "/etc/systemd/system", "/etc/xdg",
        "/var/lib", "/var/cache", "/var/log", "/etc",
    ];

    private static readonly string[] HomeExact =
    [
        ".config", ".local", ".local/share", ".local/state", ".local/bin", ".cache", ".var", ".var/app", "snap",
        ".local/share/applications", ".config/autostart", ".config/systemd", ".config/systemd/user",
        ".local/share/icons", ".local/lib",
    ];

    private static readonly string[] HomeTrees =
    [
        ".ssh", ".gnupg", ".pki", ".local/share/keyrings", ".local/share/Trash", ".local/share/flatpak",
        ".config/dconf", ".config/user-dirs.dirs", ".local/share/gvfs-metadata", ".local/share/recently-used.xbel",
    ];

    /// <param name="profiles">Usuários da máquina que a varredura cobre.</param>
    /// <param name="ownTrees">Pastas do próprio aplicativo (dados, quarentena, pasta do executável).</param>
    /// <param name="otherAppLocations">Pastas de outros programas que continuam instalados.</param>
    /// <param name="ownerOf">Devolve o pacote instalado dono do caminho, ou null.</param>
    public static PathGuard CreateGuard(
        IReadOnlyList<UnixProfile> profiles,
        IEnumerable<string> ownTrees,
        IEnumerable<string> otherAppLocations,
        Func<string, string?>? ownerOf)
    {
        var exact = new List<string>(ProtectedSystemExact);
        var trees = new List<string>(ProtectedSystemTrees);
        trees.AddRange(ownTrees);

        foreach (var profile in profiles)
        {
            exact.Add(profile.Home);
            exact.AddRange(HomeExact.Select(c => UnixPath.Combine(profile.Home, c)));
            trees.AddRange(HomeTrees.Select(c => UnixPath.Combine(profile.Home, c)));
            trees.AddRange(profile.PersonalFolders);
        }

        var homes = profiles.Select(p => UnixPath.Normalize(p.Home)).ToList();

        string? Veto(string path)
        {
            var home = homes.FirstOrDefault(h => UnixPath.IsStrictlyUnder(path, h));
            if (home is not null)
            {
                // Na pasta pessoal só valem as pastas ocultas (configuração de programas) e ~/snap.
                // "Documentos", "Projetos" e afins nunca são candidatos, nem o que estiver dentro deles.
                var first = path[(home.Length + 1)..].Split('/')[0];
                if (!first.StartsWith('.') && first != "snap")
                {
                    return "Pasta comum da pasta pessoal (só pastas ocultas podem ser sobras)";
                }
            }
            else if (!AllowedSystemRoots.Any(root => UnixPath.IsStrictlyUnder(path, root)))
            {
                return "Fora das áreas permitidas para sobras";
            }

            return ownerOf?.Invoke(path) is { Length: > 0 } owner
                ? $"Pertence ao pacote instalado '{owner}'"
                : null;
        }

        return new PathGuard(exact, trees, AllowedInsideProtected, otherAppLocations, UnixPathRules.Instance, Veto);
    }
}
