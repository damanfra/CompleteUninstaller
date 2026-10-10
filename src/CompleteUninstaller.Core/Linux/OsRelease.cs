using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.Core.Linux;

/// <summary>
/// Dados de /etc/os-release. Servem para reconhecer o que é "da distribuição": o mantenedor dos pacotes
/// (Ubuntu Developers, Debian Maintainers, Fedora Project...) varia por distro, mas o os-release diz qual é a atual
/// e de quais ela deriva (ID_LIKE), então a regra funciona em qualquer uma.
/// </summary>
public sealed class OsRelease
{
    /// <summary>Palavras que identificam o fabricante de pacotes de cada família (chave: ID ou ID_LIKE, em minúsculas).</summary>
    private static readonly Dictionary<string, string[]> KnownVendors = new(StringComparer.Ordinal)
    {
        ["ubuntu"] = ["ubuntu", "canonical"],
        ["debian"] = ["debian"],
        ["fedora"] = ["fedora"],
        ["rhel"] = ["red hat"],
        ["centos"] = ["centos", "red hat"],
        ["rocky"] = ["rocky"],
        ["almalinux"] = ["almalinux"],
        ["linuxmint"] = ["linux mint"],
        ["suse"] = ["suse", "opensuse"],
        ["opensuse"] = ["suse", "opensuse"],
        ["sles"] = ["suse"],
        ["arch"] = ["arch linux"],
        ["pop"] = ["system76"],
        ["elementary"] = ["elementary"],
        ["kali"] = ["kali"],
        ["raspbian"] = ["raspbian", "raspberry pi"],
    };

    private readonly string[] _vendorPhrases;

    public OsRelease(string id, IReadOnlyList<string> idLike, string name)
    {
        Id = id;
        IdLike = idLike;
        Name = name;

        var phrases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { id }.Concat(idLike))
        {
            var normalized = key.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                continue;
            }

            phrases.Add(normalized);
            foreach (var (known, words) in KnownVendors)
            {
                if (normalized == known || normalized.StartsWith(known + "-", StringComparison.Ordinal))
                {
                    phrases.UnionWith(words);
                }
            }
        }

        var nameWords = NameNormalizer.Normalize(name);
        if (nameWords.Length >= 4 && nameWords is not ("linux" or "gnu linux"))
        {
            phrases.Add(nameWords);
        }

        _vendorPhrases = phrases.Where(p => p.Length >= 3).ToArray();
    }

    public string Id { get; }

    public IReadOnlyList<string> IdLike { get; }

    /// <summary>"Ubuntu", "Fedora Linux"...</summary>
    public string Name { get; }

    public static OsRelease Unknown { get; } = new(string.Empty, [], string.Empty);

    public static OsRelease Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (line.Length == 0 || line[0] == '#' || eq <= 0)
            {
                continue;
            }

            values[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"', '\'');
        }

        values.TryGetValue("ID", out var id);
        values.TryGetValue("ID_LIKE", out var like);
        values.TryGetValue("NAME", out var name);
        return new OsRelease(
            id ?? string.Empty,
            (like ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries),
            name ?? string.Empty);
    }

    /// <summary>
    /// O fabricante é a própria distribuição (ou a de que ela deriva)? Compara palavras inteiras:
    /// "Ubuntu Developers" sim; "Ubuntuzilla" não.
    /// </summary>
    public bool IsDistroPublisher(string? publisher)
    {
        var normalized = NameNormalizer.Normalize(publisher);
        if (normalized.Length == 0 || _vendorPhrases.Length == 0)
        {
            return false;
        }

        var haystack = $" {normalized} ";
        return _vendorPhrases.Any(p => haystack.Contains($" {p} ", StringComparison.Ordinal));
    }
}
