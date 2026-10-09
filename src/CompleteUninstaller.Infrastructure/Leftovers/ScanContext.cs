using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Core.Text;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>Pasta de instalação (registrada ou deduzida) do programa alvo.</summary>
internal sealed record InstallDirCandidate(string Path, Confidence Confidence, string Reason);

/// <summary>Tudo o que os scanners precisam saber sobre o programa alvo e o resto da máquina.</summary>
internal sealed class ScanContext
{
    private ScanContext(
        InstalledApp target,
        AppNameMatcher matcher,
        PathGuard guard,
        SystemPaths paths,
        IReadOnlyList<AppNameMatcher> otherAppMatchers,
        HashSet<string> otherPublishers)
    {
        Target = target;
        Matcher = matcher;
        Guard = guard;
        Paths = paths;
        OtherAppMatchers = otherAppMatchers;
        OtherPublishers = otherPublishers;
    }

    public InstalledApp Target { get; }

    public AppNameMatcher Matcher { get; }

    public PathGuard Guard { get; }

    public SystemPaths Paths { get; }

    /// <summary>Matchers dos demais programas instalados, para não confundir sobras de um com pastas de outro.</summary>
    public IReadOnlyList<AppNameMatcher> OtherAppMatchers { get; }

    /// <summary>Fabricantes (normalizados) de outros programas instalados.</summary>
    public HashSet<string> OtherPublishers { get; }

    public List<InstallDirCandidate> InstallDirs { get; } = [];

    public static ScanContext Create(InstalledApp target, IReadOnlyList<InstalledApp> inventory, SystemPaths paths)
    {
        var others = inventory.Where(a => !IsSameApp(a, target)).ToList();

        // O nome da pasta de instalação vira um nome alternativo só se já se parecer com o programa
        // (evita que "C:\Program Files\Adobe" transforme toda pasta "Adobe" em sobra).
        var baseMatcher = new AppNameMatcher(target.DisplayName, target.Publisher);
        var alternativeNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(target.InstallLocation))
        {
            var leaf = WinPath.GetLeaf(target.InstallLocation);
            if (baseMatcher.Match(leaf) is NameMatch.Exact or NameMatch.Partial)
            {
                alternativeNames.Add(leaf);
            }
        }

        if (!string.IsNullOrWhiteSpace(target.PackageFamilyName))
        {
            alternativeNames.Add(target.PackageFamilyName.Split('_')[0]);
        }

        var matcher = new AppNameMatcher(target.DisplayName, target.Publisher, alternativeNames);

        var otherLocations = others.SelectMany(KnownDirectories).ToList();
        var guard = SafetyRules.CreateGuard(paths, otherLocations);

        var otherMatchers = others
            .Where(a => !a.IsUpdate)
            .Select(a => new AppNameMatcher(a.DisplayName, a.Publisher))
            .ToList();

        var otherPublishers = others
            .Select(a => NameNormalizer.Compact(NameNormalizer.NormalizePublisher(a.Publisher)))
            .Where(p => p.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);

        var context = new ScanContext(target, matcher, guard, paths, otherMatchers, otherPublishers);
        context.InstallDirs.AddRange(InstallDirResolver.Resolve(context));

        Log.Info($"Contexto de varredura para '{target.DisplayName}': {others.Count} outros programas, " +
                 $"pastas de instalação: {string.Join("; ", context.InstallDirs.Select(d => $"{d.Path} ({d.Confidence})"))}");
        return context;
    }

    /// <summary>O nome pertence (de forma exata) a outro programa que continua instalado.</summary>
    public bool BelongsToAnotherApp(string name) =>
        OtherAppMatchers.Any(m => m.Match(name) == NameMatch.Exact);

    public InstallDirCandidate? FindInstallDir(string? path) =>
        string.IsNullOrWhiteSpace(path) || !WinPath.IsRootedLocal(path)
            ? null
            : InstallDirs.FirstOrDefault(d => WinPath.IsSameOrUnder(path, d.Path));

    /// <summary>Adiciona um item de arquivo/pasta passando pela proteção de caminhos.</summary>
    public bool TryAddPath(LeftoverCollector collector, LeftoverKind kind, string path, Confidence confidence, string reason)
    {
        if (!Guard.CanRemove(path, out var why))
        {
            Log.Info($"Protegido, ignorado: {path} — {why}");
            return false;
        }

        if (FileSystemHelper.IsReparsePoint(path))
        {
            Log.Info($"Link/junção, ignorado: {path}");
            return false;
        }

        collector.Add(new LeftoverItem { Kind = kind, Target = path, Confidence = confidence, Reason = reason });
        return true;
    }

    /// <summary>Mesmo programa = mesma entrada, mesmo produto MSI/pacote, ou mesmo nome e versão.</summary>
    private static bool IsSameApp(InstalledApp a, InstalledApp target)
    {
        if (a.Id == target.Id)
        {
            return true;
        }

        if (a.MsiProductCode is not null
            && string.Equals(a.MsiProductCode, target.MsiProductCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (a.PackageFullName is not null && a.PackageFullName == target.PackageFullName)
        {
            return true;
        }

        return NameNormalizer.Normalize(a.DisplayName) == NameNormalizer.Normalize(target.DisplayName)
            && string.Equals(a.DisplayVersion, target.DisplayVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pastas que sabidamente pertencem a um programa (registrada, do ícone e do desinstalador).</summary>
    private static IEnumerable<string> KnownDirectories(InstalledApp app)
    {
        if (ValueOrNull(app.InstallLocation) is { } location)
        {
            yield return location;
        }

        foreach (var file in new[]
                 {
                     CommandLineParser.ExtractIconPath(app.DisplayIcon),
                     CommandLineParser.Parse(app.UninstallString, File.Exists)?.FileName,
                 })
        {
            var expanded = ValueOrNull(file);
            if (expanded is null || !WinPath.IsRootedLocal(expanded))
            {
                continue;
            }

            if (WinPath.GetParent(expanded) is { } parent)
            {
                yield return parent;
            }
        }
    }

    private static string? ValueOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
}
