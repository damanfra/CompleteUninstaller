using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Infrastructure.Linux.Native;

namespace CompleteUninstaller.Infrastructure.Linux;

/// <summary>Pastas do próprio Complete Uninstaller no Linux.</summary>
public static class AppPaths
{
    public const string SystemDataRoot = "/var/lib/CompleteUninstaller";

    /// <summary>Quarentena de itens do sistema (/opt, /etc...), mantida pelo auxiliar com privilégios.</summary>
    public const string SystemQuarantineRoot = SystemDataRoot + "/Quarantine";

    public static string UserDataRoot
    {
        get
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(dataHome) && dataHome.StartsWith('/')
                ? dataHome
                : Path.Combine(LinuxEnvironment.HomeDirectory, ".local", "share");
            return Path.Combine(baseDir, "CompleteUninstaller");
        }
    }

    public static string QuarantineRoot => Path.Combine(UserDataRoot, "Quarantine");

    public static string LogsRoot => Path.Combine(UserDataRoot, "Logs");
}

/// <summary>Quem está executando e quais pastas pessoais a varredura cobre.</summary>
public sealed class LinuxEnvironment
{
    private static readonly string[] DefaultPersonalFolders =
        ["Desktop", "Documents", "Downloads", "Music", "Pictures", "Videos", "Templates", "Public"];

    private LinuxEnvironment(string userName, string home, bool isRoot, IReadOnlyList<UnixProfile> profiles, OsRelease distro)
    {
        Distro = distro;
        UserName = userName;
        Home = home;
        IsRoot = isRoot;
        Profiles = profiles;
    }

    /// <summary>A distribuição em uso (de /etc/os-release).</summary>
    public OsRelease Distro { get; }

    public string UserName { get; }

    public string Home { get; }

    /// <summary>Executando como root (por exemplo, o auxiliar iniciado pelo pkexec).</summary>
    public bool IsRoot { get; }

    /// <summary>Usuários cobertos pela varredura: apenas o usuário atual.</summary>
    public IReadOnlyList<UnixProfile> Profiles { get; }

    public static string HomeDirectory
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            return !string.IsNullOrWhiteSpace(home) && home.StartsWith('/')
                ? UnixPath.Normalize(home)
                : UnixPath.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }

    public static LinuxEnvironment Detect()
    {
        var home = HomeDirectory;
        var user = Environment.UserName;
        var personal = ReadUserDirs(home).Concat(DefaultPersonalFolders.Select(f => UnixPath.Combine(home, f)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new LinuxEnvironment(user, home, Libc.IsRoot(), [new UnixProfile(user, home, personal)], ReadOsRelease());
    }

    private static OsRelease ReadOsRelease()
    {
        foreach (var file in new[] { "/etc/os-release", "/usr/lib/os-release" })
        {
            try
            {
                if (File.Exists(file))
                {
                    return OsRelease.Parse(File.ReadAllText(file));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return OsRelease.Unknown;
    }

    /// <summary>~/.config/user-dirs.dirs: XDG_DOCUMENTS_DIR="$HOME/Documentos" (pastas com nome traduzido).</summary>
    private static IEnumerable<string> ReadUserDirs(string home)
    {
        var file = Path.Combine(home, ".config", "user-dirs.dirs");
        string[] lines;
        try
        {
            lines = File.Exists(file) ? File.ReadAllLines(file) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("XDG_", StringComparison.Ordinal) || !line.Contains("_DIR=", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim().Trim('"');
            value = value.Replace("$HOME", home, StringComparison.Ordinal);
            if (value.StartsWith('/') && UnixPath.IsRootedLocal(value) && !UnixPath.AreEqual(value, home))
            {
                yield return UnixPath.Normalize(value);
            }
        }
    }

    /// <summary>Pastas que o próprio aplicativo protege (dados, quarentena, onde ele está instalado).</summary>
    public IEnumerable<string> OwnTrees()
    {
        yield return AppPaths.UserDataRoot;
        yield return AppPaths.SystemDataRoot;
        yield return UnixPath.Normalize(AppContext.BaseDirectory);
    }
}
