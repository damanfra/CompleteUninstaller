namespace CompleteUninstaller.Core.Paths;

/// <summary>
/// Regras de caminho de uma plataforma. O <see cref="Safety.PathGuard"/> e o
/// <see cref="Leftovers.LeftoverCollector"/> funcionam igual nas duas; só a forma de comparar caminhos muda.
/// </summary>
public interface IPathRules
{
    /// <summary>Como comparar caminhos já normalizados (Windows ignora maiúsculas; Unix não).</summary>
    StringComparer Comparer { get; }

    string Normalize(string path);

    /// <summary>Caminho absoluto e local, em forma segura de avaliar (sem ".." no Unix; sem rede no Windows).</summary>
    bool IsRootedLocal(string path);

    /// <summary>Raiz do sistema de arquivos ("C:\" no Windows, "/" no Unix).</summary>
    bool IsRoot(string path);

    bool AreEqual(string a, string b);

    bool IsSameOrUnder(string path, string root);

    bool IsStrictlyUnder(string path, string root);
}

public sealed class WindowsPathRules : IPathRules
{
    public static readonly WindowsPathRules Instance = new();

    public StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public string Normalize(string path) => WinPath.Normalize(path);

    public bool IsRootedLocal(string path) => WinPath.IsRootedLocal(path);

    public bool IsRoot(string path) => WinPath.IsDriveRoot(path);

    public bool AreEqual(string a, string b) => WinPath.AreEqual(a, b);

    public bool IsSameOrUnder(string path, string root) => WinPath.IsSameOrUnder(path, root);

    public bool IsStrictlyUnder(string path, string root) => WinPath.IsStrictlyUnder(path, root);
}

public sealed class UnixPathRules : IPathRules
{
    public static readonly UnixPathRules Instance = new();

    public StringComparer Comparer => StringComparer.Ordinal;

    public string Normalize(string path) => UnixPath.Normalize(path);

    public bool IsRootedLocal(string path) => UnixPath.IsRootedLocal(path);

    public bool IsRoot(string path) => UnixPath.IsRoot(path);

    public bool AreEqual(string a, string b) => UnixPath.AreEqual(a, b);

    public bool IsSameOrUnder(string path, string root) => UnixPath.IsSameOrUnder(path, root);

    public bool IsStrictlyUnder(string path, string root) => UnixPath.IsStrictlyUnder(path, root);
}
