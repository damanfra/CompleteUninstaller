using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Inventory;

/// <summary>Lê os atalhos de aplicativos (.desktop) de todos os lugares usuais do sistema e do usuário.</summary>
public static class DesktopCatalog
{
    public static IReadOnlyList<string> ApplicationDirectories(LinuxEnvironment environment) =>
    [
        "/usr/share/applications",
        "/usr/local/share/applications",
        "/var/lib/flatpak/exports/share/applications",
        "/var/lib/snapd/desktop/applications",
        Path.Combine(environment.Home, ".local/share/applications"),
        Path.Combine(environment.Home, ".local/share/flatpak/exports/share/applications"),
    ];

    public static IReadOnlyList<string> AutostartDirectories(LinuxEnvironment environment) =>
        ["/etc/xdg/autostart", Path.Combine(environment.Home, ".config/autostart")];

    public static List<DesktopEntry> Load(IEnumerable<string> directories)
    {
        var entries = new List<DesktopEntry>();
        foreach (var dir in directories)
        {
            foreach (var file in FileSystemHelper.GetFiles(dir, "*.desktop"))
            {
                try
                {
                    // Tamanho razoável: .desktop é pequeno; ignora arquivos absurdos.
                    if (new FileInfo(file).Length > 256 * 1024)
                    {
                        continue;
                    }

                    if (DesktopEntry.Parse(file, File.ReadAllText(file)) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return entries;
    }

    /// <summary>Procura um ícone PNG pelo nome declarado no .desktop (ou usa o caminho, se já for absoluto).</summary>
    public static string? FindIcon(string? iconName, LinuxEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        if (iconName.StartsWith('/'))
        {
            return File.Exists(iconName) && iconName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? iconName : null;
        }

        if (iconName.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        var roots = new[]
        {
            "/usr/share/icons/hicolor",
            "/usr/local/share/icons/hicolor",
            "/var/lib/flatpak/exports/share/icons/hicolor",
            Path.Combine(environment.Home, ".local/share/icons/hicolor"),
            Path.Combine(environment.Home, ".local/share/flatpak/exports/share/icons/hicolor"),
        };
        string[] sizes = ["48x48", "64x64", "128x128", "256x256", "32x32", "512x512", "24x24"];
        foreach (var size in sizes)
        {
            foreach (var root in roots)
            {
                var candidate = Path.Combine(root, size, "apps", iconName + ".png");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var pixmaps in new[] { "/usr/share/pixmaps", Path.Combine(environment.Home, ".local/share/pixmaps") })
        {
            var candidate = Path.Combine(pixmaps, iconName + ".png");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
