using CompleteUninstaller.Core.Linux;

namespace CompleteUninstaller.Infrastructure.Linux.Platform;

/// <summary>
/// Descobre se um caminho pertence a um pacote AINDA instalado (dpkg -S / rpm -qf). É a proteção mais
/// importante do Linux: o que um pacote instalado possui nunca vira sobra, mesmo que o nome combine.
/// Na dúvida (erro do comando, lista de instalados desconhecida) o caminho fica protegido.
/// </summary>
public sealed class PackageOwnership
{
    private static readonly char[] GlobCharacters = ['*', '?', '[', '\\'];

    private readonly bool _hasDpkg = CommandRunner.Exists("dpkg-query");
    private readonly bool _hasRpm = CommandRunner.Exists("rpm");
    private readonly HashSet<string>? _installed;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    /// <param name="installedNames">
    /// Pacotes instalados (sem a arquitetura). Se informado e não vazio, só eles contam como donos: um pacote
    /// removido que deixou configuração (estado "rc") ainda aparece no dpkg -S, mas não protege nada.
    /// Vazio é tratado como "desconhecido": qualquer dono protege.
    /// </param>
    public PackageOwnership(IEnumerable<string>? installedNames = null)
    {
        var names = installedNames?.Select(ToolOutputParsers.StripArch).ToHashSet(StringComparer.Ordinal);
        _installed = names is { Count: > 0 } ? names : null;
    }

    /// <summary>Há ferramenta de pacotes para consultar posse (dpkg ou rpm).</summary>
    public bool CanQuery => _hasDpkg || _hasRpm;

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
            if (OwnershipDecision.Dpkg(result.ExitCode, result.Output, _installed) is { } owner)
            {
                return owner;
            }
        }

        if (_hasRpm)
        {
            var result = CommandRunner.Run("rpm", ["-qf", "--qf", "%{NAME}\\n", path], timeoutMs: 30_000);
            var owner = OwnershipDecision.Rpm(result.ExitCode, result.Output + result.Error);
            if (owner is not null)
            {
                return owner;
            }
        }

        return null;
    }
}
