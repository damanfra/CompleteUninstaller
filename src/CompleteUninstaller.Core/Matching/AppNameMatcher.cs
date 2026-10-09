using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.Core.Matching;

public enum NameMatch
{
    None,

    /// <summary>O nome é o do programa (após normalização).</summary>
    Exact,

    /// <summary>O nome se parece com o do programa (subconjunto ou superconjunto de palavras).</summary>
    Partial,

    /// <summary>O nome é o do fabricante (ex.: pasta "Mozilla").</summary>
    Publisher,
}

/// <summary>
/// Compara nomes de pastas, atalhos, serviços e tarefas com o nome de um programa.
/// É o coração da heurística de sobras: regras conservadoras para "Exact" (usado no nível Seguro)
/// e regras mais amplas para "Partial" (usado no nível Moderado, sempre com revisão do usuário).
/// </summary>
public sealed class AppNameMatcher
{
    /// <summary>Nomes (já compactados) que nunca identificam um programa sozinhos.</summary>
    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "commonfiles", "microsoft", "microsoftshared", "windows", "temp", "tmp", "cache", "caches", "data", "app",
        "apps", "application", "applications", "program", "programs", "programfiles", "programdata", "appdata",
        "software", "tools", "tool", "update", "updates", "updater", "setup", "install", "installer",
        "installers", "installation", "packages", "package", "packagecache", "config", "configs",
        "configuration", "settings", "user", "users", "userdata", "logs", "log", "bin", "lib", "libs",
        "plugins", "plugin", "addons", "extensions", "shared", "common", "runtime", "runtimes", "driver",
        "drivers", "system", "system32", "default", "local", "locallow", "roaming", "desktop", "documents",
        "downloads", "music", "pictures", "videos", "public", "crashdumps", "crashreports", "crashpad",
        "backup", "backups", "resources", "assets", "help", "docs", "temporary", "history", "profiles",
        "profile", "uninstall", "uninstaller", "service", "services", "support", "sdk", "sdks",
        "framework", "frameworks", "components", "component", "module", "modules", "library", "languages",
        "locales", "locale", "fonts", "icons", "images", "themes", "skins", "scripts", "templates",
        "samples", "examples", "web", "client", "server", "launcher", "helper", "host", "agent", "gpucache",
        "codecache", "localstorage", "sessionstorage", "indexeddb", "blobstorage", "network", "storage",
        "database", "db", "state", "dumps", "reports", "feedback", "telemetry", "diagnostics", "startmenu",
        "startup", "accessories", "administrativetools", "systemtools", "maintenance", "accessibility",
        "windowspowershell", "windowsapps", "windowsdefender", "windowstools", "windowssystem",
        "windowsaccessories", "windowseaseofaccess", "internetexplorer", "onedrive", "steamapps",
    };

    /// <summary>Palavras que não contam como "significativas" numa comparação parcial.</summary>
    private static readonly HashSet<string> GenericTokens = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "of", "by", "de", "da", "do", "para", "e", "y", "edition", "version", "update",
        "updates", "updater", "setup", "installer", "install", "uninstall", "app", "application", "software",
        "program", "tool", "tools", "service", "services", "helper", "launcher", "client", "plugin", "plugins",
        "runtime", "driver", "drivers", "pro", "professional", "free", "lite", "portable", "home", "standard",
        "enterprise", "community", "ultimate", "premium", "plus", "beta", "alpha", "preview", "stable",
        "release", "new", "windows", "win", "microsoft", "pc", "desktop", "user", "system", "data", "files",
        "file", "common", "manager", "center", "centre", "suite", "package", "pack", "sdk", "core",
        "framework", "library", "components", "component", "module", "x64", "x86", "64", "32",
    };

    private readonly HashSet<string> _exactCompacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nameTokens;
    private readonly HashSet<string> _publisherTokens;
    private readonly string[] _coreTokens;
    private readonly string _coreCompact;

    public AppNameMatcher(string displayName, string? publisher, IEnumerable<string>? alternativeNames = null)
    {
        NameNormalized = NameNormalizer.Normalize(displayName);
        var withParentheticals = NameNormalizer.Normalize(displayName, stripParentheticals: false);

        var publisherNormalized = NameNormalizer.NormalizePublisher(publisher);
        PublisherCompact = NameNormalizer.Compact(publisherNormalized);
        _publisherTokens = new HashSet<string>(NameNormalizer.Tokens(publisherNormalized), StringComparer.Ordinal);

        _nameTokens = new HashSet<string>(NameNormalizer.Tokens(withParentheticals), StringComparer.Ordinal);

        // "Núcleo" do nome = nome sem o fabricante no início ("Mozilla Firefox" -> "firefox").
        var tokens = NameNormalizer.Tokens(NameNormalized);
        var skip = 0;
        while (skip < tokens.Length - 1 && _publisherTokens.Contains(tokens[skip]))
        {
            skip++;
        }

        _coreTokens = tokens[skip..];
        _coreCompact = string.Concat(_coreTokens);

        AddExact(NameNormalized);
        AddExact(withParentheticals);
        if (_coreCompact.Length >= 4)
        {
            AddExact(string.Join(' ', _coreTokens));
        }

        if (alternativeNames is not null)
        {
            foreach (var alternative in alternativeNames)
            {
                AddExact(NameNormalizer.Normalize(alternative));
            }
        }
    }

    public string NameNormalized { get; }

    public string PublisherCompact { get; }

    public static bool IsGeneric(string name) =>
        GenericNames.Contains(NameNormalizer.Compact(NameNormalizer.Normalize(name)));

    public NameMatch Match(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameMatch.None;
        }

        var normalized = NameNormalizer.Normalize(name);
        var compact = NameNormalizer.Compact(normalized);
        if (compact.Length < 3 || GenericNames.Contains(compact))
        {
            return NameMatch.None;
        }

        if (_exactCompacts.Contains(compact))
        {
            return NameMatch.Exact;
        }

        if (PublisherCompact.Length >= 3 && compact == PublisherCompact)
        {
            return NameMatch.Publisher;
        }

        var tokens = NameNormalizer.Tokens(normalized);
        var significant = tokens
            .Where(t => !_publisherTokens.Contains(t) && !GenericTokens.Contains(t))
            .ToArray();
        if (significant.Length == 0 || string.Concat(significant).Length < 4)
        {
            return NameMatch.None;
        }

        // Uma palavra solta só conta se for a primeira ou a última do nome do programa
        // ("Code" para "Visual Studio Code" sim; "Studio" não).
        if (tokens.Length == 1 && _coreTokens.Length > 1
            && tokens[0] != _coreTokens[0] && tokens[0] != _coreTokens[^1])
        {
            return NameMatch.None;
        }

        // Todas as palavras do nome analisado aparecem no nome do programa.
        if (tokens.All(t => _nameTokens.Contains(t)))
        {
            return NameMatch.Partial;
        }

        // O nome analisado contém todas as palavras do núcleo do programa ("Firefox Profiles" para "Firefox").
        if (_coreTokens.Length > 0 && _coreCompact.Length >= 4 && _coreTokens.All(t => tokens.Contains(t)))
        {
            return NameMatch.Partial;
        }

        return NameMatch.None;
    }

    private void AddExact(string normalized)
    {
        var compact = NameNormalizer.Compact(normalized);
        if (compact.Length >= 3 && !GenericNames.Contains(compact))
        {
            _exactCompacts.Add(compact);
        }
    }
}
