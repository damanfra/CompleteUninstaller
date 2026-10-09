using System.Globalization;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.Core.Linux;

/// <summary>
/// Converte a saída das ferramentas de pacotes do Linux em <see cref="InstalledApp"/>. Tudo aqui é texto puro,
/// sem executar nada, para poder ser testado em qualquer sistema com amostras reais de saída.
/// </summary>
public static class PackageParsers
{
    /// <summary>Argumentos de <c>dpkg-query</c> que produzem o formato lido por <see cref="ParseDpkg"/>.</summary>
    public const string DpkgQueryFormat =
        "${db:Status-Abbrev}\\t${Package}\\t${Architecture}\\t${Version}\\t${Installed-Size}\\t${Maintainer}" +
        "\\t${Section}\\t${Priority}\\t${Essential}\\t${binary:Summary}\\t${Homepage}\\n";

    /// <summary>Formato de <c>rpm -qa --qf</c> lido por <see cref="ParseRpm"/>.</summary>
    public const string RpmQueryFormat =
        "%{NAME}\\t%{VERSION}-%{RELEASE}\\t%{ARCH}\\t%{VENDOR}\\t%{SIZE}\\t%{INSTALLTIME}\\t%{SUMMARY}\\t%{URL}\\n";

    private static readonly string[] DpkgLibrarySections =
        ["libs", "oldlibs", "libdevel", "kernel", "debug", "metapackages", "python", "perl", "ruby", "javascript", "introspection"];

    private static readonly HashSet<string> RpmCriticalNames = new(StringComparer.Ordinal)
    {
        "rpm", "dnf", "dnf5", "yum", "glibc", "systemd", "bash", "coreutils", "filesystem", "setup", "basesystem",
        "shadow-utils", "sudo", "util-linux", "kernel", "kernel-core", "kernel-modules", "grub2-common", "grub2-pc",
        "grub2-efi-x64", "openssl-libs", "libc", "zypper", "dbus", "dbus-broker", "polkit", "NetworkManager",
        "selinux-policy", "selinux-policy-targeted", "pam", "glibc-common", "systemd-libs", "rpm-libs",
    };

    private static readonly string[] RpmLibrarySuffixes =
        ["-libs", "-devel", "-common", "-data", "-langpack", "-fonts", "-doc", "-docs", "-debuginfo", "-headers"];

    private static readonly HashSet<string> SnapCoreNames = new(StringComparer.Ordinal) { "core", "snapd", "bare" };

    /// <summary>Resultado do dpkg: pacotes instalados e pacotes removidos que deixaram configuração (estado "rc").</summary>
    public sealed record DpkgResult(List<InstalledApp> Installed, List<string> ResidualConfig);

    public static DpkgResult ParseDpkg(string output, ISet<string>? manuallyInstalled = null)
    {
        var installed = new List<InstalledApp>();
        var residual = new List<string>();

        foreach (var line in Lines(output))
        {
            var f = line.Split('\t');
            if (f.Length < 9)
            {
                continue;
            }

            var status = f[0].Trim();
            var name = f[1].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (status == "rc")
            {
                residual.Add(name);
                continue;
            }

            if (status != "ii")
            {
                continue;
            }

            var arch = f[2].Trim();
            var section = Section(f[6]);
            var priority = f[7].Trim().ToLowerInvariant();
            var essential = f[8].Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
            var isManual = manuallyInstalled?.Contains(name) ?? true;

            var flags = AppFlags.None;
            if (essential || priority is "required" or "important")
            {
                flags |= AppFlags.SystemComponent | AppFlags.NoRemove;
            }

            if (DpkgLibrarySections.Contains(section) || !isManual)
            {
                flags |= AppFlags.Library;
            }

            var qualified = string.IsNullOrEmpty(arch) || arch == "all" ? name : $"{name}:{arch}";
            var app = new InstalledApp
            {
                Id = $"dpkg:{qualified}",
                DisplayName = name,
                Source = AppSource.Dpkg,
                PackageName = qualified,
                PackageArch = arch,
                DisplayVersion = f[3].Trim(),
                Publisher = CleanMaintainer(f[5]),
                EstimatedSizeBytes = long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : null,
                Flags = flags,
            };
            app.ExtraNames.Add(name);
            installed.Add(app);
        }

        return new DpkgResult(installed, residual);
    }

    public static List<InstalledApp> ParseRpm(string output)
    {
        var apps = new List<InstalledApp>();
        foreach (var line in Lines(output))
        {
            var f = line.Split('\t');
            if (f.Length < 6 || f[0] == "gpg-pubkey")
            {
                continue;
            }

            var name = f[0].Trim();
            var flags = AppFlags.None;
            if (RpmCriticalNames.Contains(name) || name.StartsWith("kernel-", StringComparison.Ordinal))
            {
                flags |= AppFlags.SystemComponent | AppFlags.NoRemove;
            }

            if (name.StartsWith("lib", StringComparison.Ordinal)
                || name.StartsWith("python3-", StringComparison.Ordinal)
                || name.StartsWith("perl-", StringComparison.Ordinal)
                || name.StartsWith("rubygem-", StringComparison.Ordinal)
                || RpmLibrarySuffixes.Any(s => name.Contains(s, StringComparison.Ordinal)))
            {
                flags |= AppFlags.Library;
            }

            var vendor = f[3].Trim();
            DateTime? installed = long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) && epoch > 0
                ? DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime
                : null;
            var arch = f[2].Trim();
            var app = new InstalledApp
            {
                Id = $"rpm:{name}.{arch}",
                DisplayName = name,
                Source = AppSource.Rpm,
                PackageName = name,
                PackageArch = arch,
                DisplayVersion = f[1].Trim(),
                Publisher = vendor is "" or "(none)" ? null : vendor,
                EstimatedSizeBytes = long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null,
                InstallDate = installed,
                Flags = flags,
            };
            app.ExtraNames.Add(name);
            apps.Add(app);
        }

        return apps;
    }

    /// <summary>
    /// Saída de <c>flatpak list --columns=application,name,version,branch,installation,origin,size</c>
    /// (separada por tabulações quando não é um terminal).
    /// </summary>
    public static List<InstalledApp> ParseFlatpak(string output, string home, bool runtimes)
    {
        var apps = new List<InstalledApp>();
        foreach (var line in Lines(output))
        {
            var f = line.Split('\t');
            if (f.Length < 5 || f[0].Trim().Length == 0 || f[0].Trim() == "Application ID")
            {
                continue;
            }

            var id = f[0].Trim();
            var branch = f[3].Trim();
            var scope = f[4].Trim();
            var origin = f.Length > 5 ? f[5].Trim() : string.Empty;
            var location = scope switch
            {
                "user" => $"{home.TrimEnd('/')}/.local/share/flatpak/app/{id}",
                "system" => $"/var/lib/flatpak/app/{id}",
                _ => null,
            };

            var app = new InstalledApp
            {
                Id = $"flatpak:{scope}:{id}:{branch}",
                DisplayName = f[1].Trim().Length > 0 ? f[1].Trim() : id,
                Source = AppSource.Flatpak,
                PackageName = id,
                PackageScope = scope,
                DisplayVersion = f[2].Trim().Length > 0 ? f[2].Trim() : branch,
                Publisher = origin.Length > 0 ? origin : null,
                InstallLocation = location,
                EstimatedSizeBytes = f.Length > 6 ? ParseHumanSize(f[6]) : null,
                PackageBranch = branch,
                Flags = runtimes ? AppFlags.Library : AppFlags.HasDesktopEntry,
            };
            app.ExtraNames.Add(id);
            if (id.Contains('.', StringComparison.Ordinal))
            {
                app.ExtraNames.Add(id[(id.LastIndexOf('.') + 1)..]); // org.mozilla.firefox -> firefox
            }

            apps.Add(app);
        }

        return apps;
    }

    /// <summary>Saída de <c>snap list</c> (colunas: Name Version Rev Tracking Publisher Notes).</summary>
    public static List<InstalledApp> ParseSnap(string output)
    {
        var apps = new List<InstalledApp>();
        var first = true;
        foreach (var line in Lines(output))
        {
            if (first)
            {
                first = false;
                if (line.TrimStart().StartsWith("Name", StringComparison.Ordinal))
                {
                    continue;
                }
            }

            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5)
            {
                continue;
            }

            var name = p[0];
            var notes = p.Length > 5 ? p[5] : "-";
            var flags = AppFlags.None;
            if (notes.Contains("base", StringComparison.Ordinal)
                || notes.Contains("snapd", StringComparison.Ordinal)
                || SnapCoreNames.Contains(name)
                || name.StartsWith("core", StringComparison.Ordinal) && name.Skip(4).All(char.IsDigit))
            {
                flags |= AppFlags.SystemComponent | AppFlags.Library;
            }
            else if (name.StartsWith("gnome-", StringComparison.Ordinal) && name.Any(char.IsDigit)
                     || name.StartsWith("gtk-common-themes", StringComparison.Ordinal)
                     || name.StartsWith("mesa-", StringComparison.Ordinal)
                     || name.StartsWith("kf5-", StringComparison.Ordinal)
                     || name.StartsWith("kde-frameworks", StringComparison.Ordinal))
            {
                flags |= AppFlags.Library;
            }

            var publisher = p[4].TrimEnd('*');
            var app = new InstalledApp
            {
                Id = $"snap:{name}",
                DisplayName = name,
                Source = AppSource.Snap,
                PackageName = name,
                DisplayVersion = p[1],
                Publisher = publisher is "" or "-" ? null : publisher,
                Flags = flags,
            };
            app.ExtraNames.Add(name);
            apps.Add(app);
        }

        return apps;
    }

    /// <summary>Converte "1.2 GB", "340.5 MB", "12 kB", "512 bytes" em bytes.</summary>
    public static long? ParseHumanSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Replace(',', '.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var unit = parts.Length > 1 ? parts[1].ToLowerInvariant() : "bytes";
        var multiplier = unit switch
        {
            "bytes" or "byte" or "b" => 1d,
            "kb" or "kib" => 1024d,
            "mb" or "mib" => 1024d * 1024,
            "gb" or "gib" => 1024d * 1024 * 1024,
            "tb" or "tib" => 1024d * 1024 * 1024 * 1024,
            _ => double.NaN,
        };
        return double.IsNaN(multiplier) ? null : (long)(value * multiplier);
    }

    /// <summary>"Ana Souza &lt;ana@exemplo.org&gt;" -> "Ana Souza".</summary>
    public static string? CleanMaintainer(string? maintainer)
    {
        if (string.IsNullOrWhiteSpace(maintainer))
        {
            return null;
        }

        var text = maintainer.Trim();
        var lt = text.IndexOf('<', StringComparison.Ordinal);
        if (lt > 0)
        {
            text = text[..lt].Trim();
        }

        return text.Length == 0 ? null : text;
    }

    private static string Section(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        var slash = s.LastIndexOf('/');
        return slash >= 0 ? s[(slash + 1)..] : s; // "universe/libs" -> "libs"
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l));
}
