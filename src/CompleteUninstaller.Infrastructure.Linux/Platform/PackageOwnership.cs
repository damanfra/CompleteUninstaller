using CompleteUninstaller.Core.Linux;

namespace CompleteUninstaller.Infrastructure.Linux.Platform;

/// <summary>
/// Descobre se um caminho pertence a um pacote AINDA instalado (dpkg -S / rpm -qf). É a proteção mais
/// importante do Linux: o que um pacote instalado possui nunca vira sobra, mesmo que o nome combine.
/// </summary>
public sealed class PackageOwnership
{
    private static readonly char[] GlobCharacters = ['*', '?', '[', '\\'];

    private readonly bool _hasDpkg = CommandRunner.Exists("dpkg-query");
    private readonly bool _hasRpm = CommandRunner.Exists("rpm");
    private readonly HashSet<string>? _installed;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    /// <param name="installedNames">
    /// Pacotes instalados (sem a arquitetura). Se informado, só eles contam como donos: um pacote removido
    /// que deixou configuração (estado "rc") ainda aparece no dpkg -S, mas não protege nada.
    /// </param>
    public PackageOwnership(IEnumerable<string>? installedNames = null) =>
        _installed = installedNames?.Select(ToolOutputParsers.StripArch).ToHashSet(StringComparer.Ordinal);

    /// <summary>Nome do pacote instalado dono do caminho; null se ninguém é dono.</summary>
    public string? OwnerOf(string path)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var owner = Query(path);
            _cache[path] = owner;
            return owner;
        }
    }

    private string? Query(string path)
    {
        if (path.IndexOfAny(GlobCharacters) >= 0)
        {
            // dpkg -S interpreta *, ? e [ como curingas; sem certeza, o caminho fica protegido.
            return "(caminho com caracteres especiais)";
        }

        if (_hasDpkg)
        {
            var result = CommandRunner.Run("dpkg-query", ["-S", path], timeoutMs: 30_000);
            if (result.Success)
            {
                var owners = ToolOutputParsers.ParseDpkgSearch(result.Output).Values.SelectMany(v => v);
                var installedOwner = owners.FirstOrDefault(o => _installed is null || _installed.Contains(o));
                if (installedOwner is not null)
                {
                    return installedOwner;
                }
            }
        }

        if (_hasRpm)
        {
            var result = CommandRunner.Run("rpm", ["-qf", "--qf", "%{NAME}\\n", path], timeoutMs: 30_000);
            if (result.Success && result.Output.Trim().Split('\n')[0].Trim() is { Length: > 0 } name)
            {
                return name;
            }
        }

        return null;
    }
}
