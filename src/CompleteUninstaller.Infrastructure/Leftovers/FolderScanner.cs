using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Text;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>
/// Procura pastas de programa e de dados/configuração: Arquivos de Programas, ProgramData e
/// AppData (Roaming, Local, LocalLow, Programs) de todos os perfis.
/// </summary>
internal static class FolderScanner
{
    public static void Scan(ScanContext context, LeftoverCollector collector, CancellationToken cancellationToken)
    {
        // Pastas de instalação conhecidas (registrada e deduzidas).
        foreach (var dir in context.InstallDirs)
        {
            if (Directory.Exists(dir.Path))
            {
                context.TryAddPath(collector, LeftoverKind.Folder, dir.Path, dir.Confidence, dir.Reason);
            }
        }

        foreach (var (root, label) in GetRoots(context.Paths))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var dir in FileSystemHelper.GetDirectories(root))
            {
                var leaf = WinPath.GetLeaf(dir);
                switch (context.Matcher.Match(leaf))
                {
                    case NameMatch.Exact when !context.BelongsToAnotherApp(leaf):
                        context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.High,
                            $"Pasta com o nome do programa em {label}");
                        break;

                    case NameMatch.Partial when !context.BelongsToAnotherApp(leaf):
                        context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.Medium,
                            $"Pasta com nome parecido com o do programa em {label}");
                        break;

                    case NameMatch.Publisher:
                        ScanPublisherFolder(context, collector, dir, label);
                        break;
                }
            }
        }

        ScanPackageCache(context, collector);
        ScanStoreData(context, collector);
    }

    /// <summary>
    /// Dentro de uma pasta do fabricante (ex.: AppData\Roaming\Mozilla), remove a subpasta do programa.
    /// A pasta do fabricante só entra se nenhum outro programa dele estiver instalado e ela não tiver
    /// mais nada além das sobras deste programa.
    /// </summary>
    private static void ScanPublisherFolder(ScanContext context, LeftoverCollector collector, string publisherDir, string label)
    {
        var subDirectories = FileSystemHelper.GetDirectories(publisherDir);
        var hasLooseFiles = FileSystemHelper.GetFiles(publisherDir).Count > 0;
        var everythingMatched = !hasLooseFiles;
        var lowest = Confidence.High;

        foreach (var sub in subDirectories)
        {
            var leaf = WinPath.GetLeaf(sub);
            var match = context.Matcher.Match(leaf);
            var added = false;
            if (match == NameMatch.Exact && !context.BelongsToAnotherApp(leaf))
            {
                added = context.TryAddPath(collector, LeftoverKind.Folder, sub, Confidence.High,
                    $"Subpasta do fabricante com o nome do programa em {label}");
            }
            else if (match == NameMatch.Partial && !context.BelongsToAnotherApp(leaf))
            {
                added = context.TryAddPath(collector, LeftoverKind.Folder, sub, Confidence.Medium,
                    $"Subpasta do fabricante com nome parecido com o do programa em {label}");
                lowest = Confidence.Medium;
            }

            everythingMatched &= added;
        }

        var publisherKey = NameNormalizer.Compact(NameNormalizer.NormalizePublisher(WinPath.GetLeaf(publisherDir)));
        if (context.OtherPublishers.Contains(publisherKey))
        {
            return; // outro programa do mesmo fabricante continua instalado
        }

        if (subDirectories.Count == 0 && !hasLooseFiles)
        {
            context.TryAddPath(collector, LeftoverKind.Folder, publisherDir, Confidence.High,
                $"Pasta vazia do fabricante em {label}");
        }
        else if (subDirectories.Count > 0 && everythingMatched)
        {
            context.TryAddPath(collector, LeftoverKind.Folder, publisherDir, lowest,
                $"Pasta do fabricante contendo apenas dados deste programa ({label})");
        }
    }

    /// <summary>ProgramData\Package Cache\{ProductCode ou BundleCode}*.</summary>
    private static void ScanPackageCache(ScanContext context, LeftoverCollector collector)
    {
        var codes = new List<string>();
        if (context.Target.MsiProductCode is { } productCode)
        {
            codes.Add(productCode);
        }

        if (context.Target.UninstallKey is { } key)
        {
            var leaf = WinPath.GetLeaf(key.SubKeyPath);
            if (CommandLineParser.IsGuid(leaf))
            {
                codes.Add(leaf);
            }
        }

        if (codes.Count == 0)
        {
            return;
        }

        foreach (var dir in FileSystemHelper.GetDirectories(context.Paths.PackageCache))
        {
            var leaf = WinPath.GetLeaf(dir);
            if (codes.Any(code => leaf.StartsWith(code, StringComparison.OrdinalIgnoreCase)))
            {
                context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.High,
                    "Cache do instalador deste produto (Package Cache)");
            }
        }
    }

    /// <summary>Dados locais de apps da Store: AppData\Local\Packages\{PackageFamilyName}.</summary>
    private static void ScanStoreData(ScanContext context, LeftoverCollector collector)
    {
        if (string.IsNullOrWhiteSpace(context.Target.PackageFamilyName))
        {
            return;
        }

        var family = context.Target.PackageFamilyName;
        var candidates = context.Paths.Profiles
            .Select(p => Path.Combine(p.Packages, family))
            .Append(Path.Combine(context.Paths.ProgramData, "Packages", family));

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.High, "Dados locais do app da Store");
            }
        }
    }

    private static IEnumerable<(string Root, string Label)> GetRoots(SystemPaths paths)
    {
        var roots = new List<(string Root, string Label)>
        {
            (paths.ProgramFiles, "Arquivos de Programas"),
            (paths.ProgramFilesX86, "Arquivos de Programas (x86)"),
            (paths.CommonProgramFiles, "Common Files"),
            (paths.CommonProgramFilesX86, "Common Files (x86)"),
            (paths.ProgramData, "ProgramData"),
        };

        foreach (var profile in paths.Profiles)
        {
            roots.Add((profile.RoamingAppData, $@"AppData\Roaming de {profile.UserName}"));
            roots.Add((profile.LocalAppData, $@"AppData\Local de {profile.UserName}"));
            roots.Add((profile.LocalLowAppData, $@"AppData\LocalLow de {profile.UserName}"));
            roots.Add((profile.LocalPrograms, $@"AppData\Local\Programs de {profile.UserName}"));
        }

        return roots
            .Where(r => !string.IsNullOrWhiteSpace(r.Root))
            .DistinctBy(r => r.Root, StringComparer.OrdinalIgnoreCase);
    }
}
