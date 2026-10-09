namespace CompleteUninstaller.Infrastructure.Linux.Platform;

/// <summary>Enumeração tolerante a erros (pastas sem permissão são simplesmente ignoradas) e que não segue links.</summary>
public static class FileSystemHelper
{
    public static IReadOnlyList<string> GetDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> GetFiles(string path, string pattern = "*")
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetFiles(path, pattern) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Link simbólico (arquivo ou pasta). Links nunca viram sobras: o destino poderia ser qualquer coisa.</summary>
    public static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Existe como arquivo, pasta ou link (mesmo quebrado).</summary>
    public static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Tamanho total de um arquivo ou pasta (limitado para não travar em árvores enormes; não segue links).</summary>
    public static long? GetSize(string path, int maxEntries = 300_000)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return null;
            }

            long total = 0;
            var entries = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0 && entries < maxEntries)
            {
                var dir = pending.Pop();
                foreach (var file in GetFiles(dir))
                {
                    entries++;
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LinkTarget is null)
                        {
                            total += info.Length;
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                    }
                }

                foreach (var sub in GetDirectories(dir))
                {
                    entries++;
                    if (!IsLink(sub))
                    {
                        pending.Push(sub);
                    }
                }
            }

            return total;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
