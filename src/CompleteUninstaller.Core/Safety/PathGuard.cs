using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Core.Safety;

/// <summary>
/// Última barreira antes de qualquer item de arquivo entrar na lista de sobras.
/// Bloqueia: raízes de unidade, pastas do sistema, áreas protegidas (Windows, dados da Microsoft, documentos)
/// e qualquer caminho que contenha — ou esteja dentro de — a pasta de outro programa ainda instalado.
/// </summary>
public sealed class PathGuard
{
    private readonly HashSet<string> _protectedExact;
    private readonly List<(string Root, bool Allowed)> _treeRules;
    private readonly List<string> _otherAppLocations;

    /// <param name="protectedExact">Pastas que não podem ser removidas, mas cujo conteúdo pode (ex.: "C:\Program Files").</param>
    /// <param name="protectedTrees">Pastas que não podem ser removidas nem ter nada removido dentro (ex.: "C:\Windows").</param>
    /// <param name="allowedTrees">Exceções dentro de áreas protegidas (ex.: o Menu Iniciar dentro de ProgramData\Microsoft). A regra mais específica vence.</param>
    /// <param name="otherAppLocations">Pastas de outros programas que continuam instalados.</param>
    public PathGuard(
        IEnumerable<string> protectedExact,
        IEnumerable<string> protectedTrees,
        IEnumerable<string> allowedTrees,
        IEnumerable<string> otherAppLocations)
    {
        _protectedExact = new HashSet<string>(
            protectedExact.Where(NotEmpty).Select(WinPath.Normalize),
            StringComparer.OrdinalIgnoreCase);

        _treeRules = protectedTrees.Where(NotEmpty).Select(p => (Root: WinPath.Normalize(p), Allowed: false))
            .Concat(allowedTrees.Where(NotEmpty).Select(p => (Root: WinPath.Normalize(p), Allowed: true)))
            .OrderByDescending(r => r.Root.Length)
            .ToList();

        _otherAppLocations = otherAppLocations
            .Where(NotEmpty)
            .Select(WinPath.Normalize)
            .Where(p => WinPath.IsRootedLocal(p) && !WinPath.IsDriveRoot(p) && !_protectedExact.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool CanRemove(string path) => CanRemove(path, out _);

    public bool CanRemove(string path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path) || !WinPath.IsRootedLocal(path))
        {
            reason = "Caminho inválido ou de rede";
            return false;
        }

        var p = WinPath.Normalize(path);
        if (WinPath.IsDriveRoot(p))
        {
            reason = "Raiz de unidade";
            return false;
        }

        if (_protectedExact.Contains(p))
        {
            reason = "Pasta do sistema protegida";
            return false;
        }

        foreach (var (root, allowed) in _treeRules)
        {
            if (!WinPath.IsSameOrUnder(p, root))
            {
                continue;
            }

            if (allowed && !WinPath.AreEqual(p, root))
            {
                break; // a regra mais específica é uma exceção permitida
            }

            reason = allowed ? "Pasta do sistema protegida" : "Dentro de uma área protegida do sistema";
            return false;
        }

        foreach (var other in _otherAppLocations)
        {
            if (WinPath.IsSameOrUnder(other, p))
            {
                reason = $"Contém a pasta de outro programa instalado ({other})";
                return false;
            }

            if (WinPath.IsSameOrUnder(p, other))
            {
                reason = $"Fica dentro da pasta de outro programa instalado ({other})";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool NotEmpty(string? value) => !string.IsNullOrWhiteSpace(value);
}
