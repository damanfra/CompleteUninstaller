namespace CompleteUninstaller.Core.Linux;

/// <summary>Os campos de um arquivo .desktop que interessam ao desinstalador.</summary>
public sealed record DesktopEntry(
    string Path,
    string Name,
    string? Exec,
    string? Icon,
    bool Hidden,
    string? Type)
{
    /// <summary>Executável principal do comando (sem "env VAR=x", sem aspas e sem códigos %u/%F).</summary>
    public string? ExecProgram => ExtractProgram(Exec);

    /// <summary>Nome do arquivo sem a extensão ("org.gnome.Calculator.desktop" → "org.gnome.Calculator").</summary>
    public string Stem
    {
        get
        {
            var leaf = Paths.UnixPath.GetLeaf(Path);
            return leaf.EndsWith(".desktop", StringComparison.Ordinal) ? leaf[..^8] : leaf;
        }
    }

    public static DesktopEntry? Parse(string path, string content)
    {
        var inEntry = false;
        string? name = null, exec = null, icon = null, type = null;
        var hidden = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (inEntry)
                {
                    break; // só a seção principal interessa
                }

                inEntry = line == "[Desktop Entry]";
                continue;
            }

            if (!inEntry)
            {
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "Name": name = value; break;
                case "Exec": exec = value; break;
                case "Icon": icon = value; break;
                case "Type": type = value; break;
                case "NoDisplay" or "Hidden": hidden |= value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            }
        }

        return name is null ? null : new DesktopEntry(path, name, exec, icon, hidden, type);
    }

    public static string? ExtractProgram(string? exec)
    {
        if (string.IsNullOrWhiteSpace(exec))
        {
            return null;
        }

        foreach (var token in Tokenize(exec))
        {
            if (token == "env" || token.Contains('=', StringComparison.Ordinal) && !token.StartsWith('/'))
            {
                continue;
            }

            return token.StartsWith('%') ? null : token;
        }

        return null;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        foreach (var c in text)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
