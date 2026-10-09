using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Core.Safety;

/// <summary>
/// Última barreira antes de qualquer item de arquivo entrar na lista de sobras.
/// Bloqueia: raízes de unidade, pastas do sistema, áreas protegidas (Windows, dados da Microsoft, documentos)
/// e qualquer caminho que contenha — ou esteja dentro de — a pasta de outro programa ainda instalado.
/// No Linux (<see cref="UnixPathRules"/>) um veto extra bloqueia caminhos que pertencem a um pacote ainda instalado.
/// </summary>
public sealed class PathGuard
{
    private readonly IPathRules _rules;
    private readonly Func<string, string?>? _extraVeto;
    private readonly HashSet<string> _protectedExact;
    private readonly List<(string Root, bool Allowed)> _treeRules;
    private readonly List<string> _otherAppLocations;

    /// <param name="protectedExact">Pastas que não podem ser removidas, mas cujo conteúdo pode (ex.: "C:\Program Files").</param>
    /// <param name="protectedTrees">Pastas que não podem ser removidas nem ter nada removido dentro (ex.: "C:\Windows").</param>
    /// <param name="allowedTrees">Exceções dentro de áreas protegidas (ex.: o Menu Iniciar dentro de ProgramData\Microsoft). A regra mais específica vence.</param>
    /// <param name="otherAppLocations">Pastas de outros programas que continuam instalados.</param>
    /// <param name="rules">Regras de caminho; o padrão é o formato do Windows.</param>
    /// <param name="extraVeto">Devolve o motivo para bloquear o caminho, ou null para liberar (no Linux: pacote dono, regras da pasta pessoal).</param>
    public PathGuard(
        IEnumerable<string> protectedExact,
        IEnumerable<string> protectedTrees,
        IEnumerable<string> allowedTrees,
        IEnumerable<string> otherAppLocations,
        IPathRules? rules = null,
        Func<string, string?>? extraVeto = null)
    {
        _rules = rules ?? WindowsPathRules.Instance;
        _extraVeto = extraVeto;

        _protectedExact = new HashSet<string>(
            protectedExact.Where(NotEmpty).Select(_rules.Normalize),
            _rules.Comparer);

        _treeRules = protectedTrees.Where(NotEmpty).Select(p => (Root: _rules.Normalize(p), Allowed: false))
            .Concat(allowedTrees.Where(NotEmpty).Select(p => (Root: _rules.Normalize(p), Allowed: true)))
            .OrderByDescending(r => r.Root.Length)
            .ToList();

        _otherAppLocations = otherAppLocations
            .Where(NotEmpty)
            .Select(_rules.Normalize)
            .Where(p => _rules.IsRootedLocal(p) && !_rules.IsRoot(p) && !_protectedExact.Contains(p))
            .Distinct(_rules.Comparer)
            .ToList();
    }

    public bool CanRemove(string path) => CanRemove(path, out _);

    public bool CanRemove(string path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path) || !_rules.IsRootedLocal(path))
        {
            reason = "Caminho inválido ou de rede";
            return false;
        }

        var p = _rules.Normalize(path);
        if (_rules.IsRoot(p))
        {
            reason = "Raiz do sistema de arquivos";
            return false;
        }

        if (_protectedExact.Contains(p))
        {
            reason = "Pasta do sistema protegida";
            return false;
        }

        foreach (var (root, allowed) in _treeRules)
        {
            if (!_rules.IsSameOrUnder(p, root))
            {
                continue;
            }

            if (allowed && !_rules.AreEqual(p, root))
            {
                break; // a regra mais específica é uma exceção permitida
            }

            reason = allowed ? "Pasta do sistema protegida" : "Dentro de uma área protegida do sistema";
            return false;
        }

        foreach (var other in _otherAppLocations)
        {
            if (_rules.IsSameOrUnder(other, p))
            {
                reason = $"Contém a pasta de outro programa instalado ({other})";
                return false;
            }

            if (_rules.IsSameOrUnder(p, other))
            {
                reason = $"Fica dentro da pasta de outro programa instalado ({other})";
                return false;
            }
        }

        if (_extraVeto?.Invoke(p) is { Length: > 0 } veto)
        {
            reason = veto;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool NotEmpty(string? value) => !string.IsNullOrWhiteSpace(value);
}
