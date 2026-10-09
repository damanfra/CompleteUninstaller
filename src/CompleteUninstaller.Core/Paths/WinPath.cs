namespace CompleteUninstaller.Core.Paths;

/// <summary>
/// Operações sobre caminhos no formato do Windows feitas apenas com texto
/// (independem da plataforma em que o código roda, o que permite testá-las).
/// Comparações são sempre sem diferenciar maiúsculas/minúsculas.
/// </summary>
public static class WinPath
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var p = path.Trim().Trim('"').Trim().Replace('/', '\\');
        var prefix = p.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\" : string.Empty;
        var body = p[prefix.Length..];
        while (body.Contains(@"\\", StringComparison.Ordinal))
        {
            body = body.Replace(@"\\", @"\", StringComparison.Ordinal);
        }

        p = prefix + body;
        if (p.Length > 3 && p.EndsWith('\\'))
        {
            p = p.TrimEnd('\\');
        }

        if (p.Length == 2 && p[1] == ':')
        {
            p += '\\';
        }

        return p;
    }

    /// <summary>Caminho absoluto local do tipo "C:\...".</summary>
    public static bool IsRootedLocal(string path)
    {
        var p = Normalize(path);
        return p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\';
    }

    public static bool IsDriveRoot(string path)
    {
        var p = Normalize(path);
        return p.Length == 3 && char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\';
    }

    public static bool AreEqual(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Verdadeiro se <paramref name="path"/> é igual a <paramref name="root"/> ou está dentro dele.</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!r.EndsWith('\\'))
        {
            r += '\\';
        }

        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrictlyUnder(string path, string root) =>
        IsSameOrUnder(path, root) && !AreEqual(path, root);

    public static string? GetParent(string path)
    {
        var p = Normalize(path);
        var idx = p.LastIndexOf('\\');
        if (idx < 0)
        {
            return null;
        }

        // "C:\Pasta" -> "C:\"   |   "C:\" -> null
        if (idx == 2 && p[1] == ':')
        {
            return p.Length > 3 ? p[..3] : null;
        }

        return idx == 0 ? null : p[..idx];
    }

    public static string GetLeaf(string path)
    {
        var p = Normalize(path);
        var idx = p.LastIndexOf('\\');
        return idx < 0 ? p : p[(idx + 1)..];
    }

    public static string Combine(string first, string second) =>
        Normalize(Normalize(first).TrimEnd('\\') + "\\" + second.TrimStart('\\', '/'));
}
