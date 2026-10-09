namespace CompleteUninstaller.Core.Paths;

/// <summary>
/// Operações sobre caminhos no formato Unix feitas apenas com texto (independem da plataforma em que o
/// código roda, o que permite testá-las no Windows). Comparações diferenciam maiúsculas de minúsculas.
/// Caminhos com segmentos ".." são tratados como inválidos: a proteção é uma checagem de texto e
/// "/home/ana/.config/x/../.." passaria por ela.
/// </summary>
public static class UnixPath
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var p = path.Trim();
        if (p.Length == 0)
        {
            return p;
        }

        var absolute = p[0] == '/';
        var segments = p.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != ".")
            .ToArray();
        var joined = string.Join('/', segments);
        return absolute ? "/" + joined : joined;
    }

    /// <summary>Caminho absoluto, sem ".." e sem caractere nulo.</summary>
    public static bool IsRootedLocal(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        var p = Normalize(path);
        return p.Length > 0 && p[0] == '/' && !p.Split('/').Contains("..");
    }

    public static bool IsRoot(string path) => Normalize(path) == "/";

    public static bool AreEqual(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>Verdadeiro se <paramref name="path"/> é igual a <paramref name="root"/> ou está dentro dele.</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, StringComparison.Ordinal) || r == "/")
        {
            return true;
        }

        return p.StartsWith(r + "/", StringComparison.Ordinal);
    }

    public static bool IsStrictlyUnder(string path, string root) =>
        IsSameOrUnder(path, root) && !AreEqual(path, root);

    public static string? GetParent(string path)
    {
        var p = Normalize(path);
        if (p is "/" or "")
        {
            return null;
        }

        var idx = p.LastIndexOf('/');
        return idx < 0 ? null : idx == 0 ? "/" : p[..idx];
    }

    public static string GetLeaf(string path)
    {
        var p = Normalize(path);
        var idx = p.LastIndexOf('/');
        return idx < 0 ? p : p[(idx + 1)..];
    }

    public static string Combine(string first, string second)
    {
        var f = Normalize(first);
        return Normalize((f == "/" ? string.Empty : f) + "/" + second.TrimStart('/'));
    }
}
