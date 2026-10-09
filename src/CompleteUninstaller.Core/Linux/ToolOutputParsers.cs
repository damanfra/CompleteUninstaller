using System.Text.RegularExpressions;

namespace CompleteUninstaller.Core.Linux;

/// <summary>Leitura da saída de apt, dnf, dpkg e rpm usada na simulação, na posse de arquivos e nas sobras de configuração.</summary>
public static partial class ToolOutputParsers
{
    [GeneratedRegex(@"^([^\s,]+(?:, [^\s,]+)*): (/.*)$")]
    private static partial Regex DpkgSearchLine();

    /// <summary><c>apt-get -s remove X</c>: linhas "Remv nome:arch [versão]" são os pacotes que seriam removidos.</summary>
    public static List<string> ParseAptSimulation(string output)
    {
        var removed = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("Remv ", StringComparison.Ordinal) && !line.StartsWith("Purg ", StringComparison.Ordinal))
            {
                continue;
            }

            var name = line[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(name))
            {
                removed.Add(name);
            }
        }

        return removed;
    }

    /// <summary>
    /// <c>dnf remove --assumeno X</c>: lê as seções "Removing:", "Removing dependent packages:" e
    /// "Removing unused dependencies:" (dnf 4 e dnf 5). Cada linha de pacote começa com um espaço.
    /// </summary>
    public static List<string> ParseDnfSimulation(string output)
    {
        var removed = new List<string>();
        var inSection = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Removing", StringComparison.Ordinal) && trimmed.EndsWith(':'))
            {
                inSection = true;
                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith("Transaction Summary", StringComparison.Ordinal))
            {
                inSection = false;
                continue;
            }

            if (inSection && line.Length > 1 && line[0] == ' ' && line[1] != ' ')
            {
                var name = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                removed.Add(name);
            }
        }

        return removed;
    }

    /// <summary><c>dpkg -S caminho...</c>: mapa caminho → pacotes donos. Linhas de erro e desvios são ignoradas.</summary>
    public static Dictionary<string, List<string>> ParseDpkgSearch(string output)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var match = DpkgSearchLine().Match(raw.TrimEnd('\r'));
            if (!match.Success)
            {
                continue;
            }

            var owners = match.Groups[1].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries)
                .Select(StripArch)
                .ToList();
            map[match.Groups[2].Value] = owners;
        }

        return map;
    }

    /// <summary>
    /// <c>dpkg -L pacote</c> ou <c>rpm -ql pacote</c>: um caminho por linha. A lista inclui TODAS as pastas-pai
    /// ("/.", "/usr", "/usr/share/applications"...), por isso ela só indica candidatos; nunca prova posse.
    /// </summary>
    public static List<string> ParseFileList(string output) =>
        output.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith('/') && l != "/." && l != "/")
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary><c>dpkg-query -W -f='${Conffiles}'</c>: linhas " /etc/foo.conf 0123abcd [obsolete]".</summary>
    public static List<string> ParseConffiles(string output)
    {
        var paths = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            var trimmed = raw.Trim();
            if (!trimmed.StartsWith('/'))
            {
                continue;
            }

            var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
            paths.Add(space < 0 ? trimmed : trimmed[..space]);
        }

        return paths;
    }

    /// <summary>Pacote dono de cada caminho consultado com <c>rpm -qf --qf '%{NAME}\n' a b c</c>, na mesma ordem.</summary>
    public static Dictionary<string, string> ParseRpmOwners(IReadOnlyList<string> paths, string output)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lines.Count != paths.Count)
        {
            return map; // saída fora do esperado: melhor não afirmar nada
        }

        for (var i = 0; i < paths.Count; i++)
        {
            if (!lines[i].Contains(" is not owned by", StringComparison.Ordinal)
                && !lines[i].StartsWith("error:", StringComparison.Ordinal)
                && !lines[i].StartsWith("file ", StringComparison.Ordinal))
            {
                map[paths[i]] = lines[i];
            }
        }

        return map;
    }

    /// <summary>Nomes de pacote do <c>apt-mark showmanual</c> (um por linha).</summary>
    public static HashSet<string> ParseNameList(string output) =>
        output.Split('\n')
            .Select(l => StripArch(l.Trim()))
            .Where(l => l.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    public static string StripArch(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? name : name[..colon];
    }
}
