namespace CompleteUninstaller.Infrastructure.Platform;

/// <summary>Enumeração tolerante a erros (pastas sem permissão são simplesmente ignoradas).</summary>
internal static class FileSystemHelper
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

    /// <summary>Arquivos até <paramref name="maxDepth"/> níveis abaixo, sem atravessar junções/links.</summary>
    public static IEnumerable<string> GetFilesRecursive(string root, string pattern, int maxDepth)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Pop();
            foreach (var file in GetFiles(dir, pattern))
            {
                yield return file;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var sub in GetDirectories(dir))
            {
                if (!IsReparsePoint(sub))
                {
                    pending.Push((sub, depth + 1));
                }
            }
        }
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Tamanho total de um arquivo ou pasta (limitado para não travar em árvores enormes).</summary>
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
                        total += new FileInfo(file).Length;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                    }
                }

                foreach (var sub in GetDirectories(dir))
                {
                    entries++;
                    if (!IsReparsePoint(sub))
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
