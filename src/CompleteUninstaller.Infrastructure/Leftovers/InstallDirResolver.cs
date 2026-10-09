using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>
/// Descobre as pastas de instalação do programa alvo: a registrada (InstallLocation) e as deduzidas
/// do ícone, do desinstalador e do cache do instalador.
/// </summary>
internal static class InstallDirResolver
{
    /// <summary>Subpastas "técnicas" que não dizem nada sobre o programa; sobe um nível quando encontradas.</summary>
    private static readonly HashSet<string> TransparentLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "bin32", "x64", "x86", "uninstall", "uninst", "uninstaller", "_uninstall", "app",
        "application", "program", "current", "setup", "installer", "uninstall information",
    };

    public static List<InstallDirCandidate> Resolve(ScanContext context)
    {
        var app = context.Target;
        var result = new List<InstallDirCandidate>();

        void Add(string? dir, Confidence confidence, string reason)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            var normalized = WinPath.Normalize(Environment.ExpandEnvironmentVariables(dir));
            if (!WinPath.IsRootedLocal(normalized) || !context.Guard.CanRemove(normalized))
            {
                return;
            }

            var existing = result.FindIndex(r => WinPath.AreEqual(r.Path, normalized));
            if (existing >= 0)
            {
                if (confidence > result[existing].Confidence)
                {
                    result[existing] = new InstallDirCandidate(normalized, confidence, reason);
                }

                return;
            }

            result.Add(new InstallDirCandidate(normalized, confidence, reason));
        }

        // 1) Pasta registrada. Se o nome da pasta for só o do fabricante (ex.: "C:\Program Files\Adobe")
        //    ou de outro programa, pode ser compartilhada: rebaixa para confiança média.
        if (app.Source != AppSource.Store && !string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            var leaf = WinPath.GetLeaf(app.InstallLocation);
            var match = context.Matcher.Match(leaf);
            var shared = match == NameMatch.Publisher || context.BelongsToAnotherApp(leaf);
            Add(app.InstallLocation,
                shared ? Confidence.Medium : Confidence.High,
                shared ? "Pasta de instalação registrada (pode ser compartilhada pelo fabricante)" : "Pasta de instalação registrada pelo programa");
        }

        // 2) Pasta do ícone e do desinstalador.
        var files = new[]
        {
            CommandLineParser.ExtractIconPath(app.DisplayIcon),
            CommandLineParser.Parse(app.UninstallString, File.Exists)?.FileName,
            CommandLineParser.Parse(app.QuietUninstallString, File.Exists)?.FileName,
        };

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(file);
            if (!WinPath.IsRootedLocal(expanded) || CommandLineParser.IsMsiExec(expanded))
            {
                continue;
            }

            var dir = WinPath.GetParent(expanded);
            for (var i = 0; i < 2 && dir is not null && TransparentLeaves.Contains(WinPath.GetLeaf(dir)); i++)
            {
                dir = WinPath.GetParent(dir);
            }

            if (dir is null)
            {
                continue;
            }

            var dirLeaf = WinPath.GetLeaf(dir);
            var match = context.Matcher.Match(dirLeaf);
            if (match == NameMatch.Exact && !context.BelongsToAnotherApp(dirLeaf))
            {
                Add(dir, Confidence.High, "Pasta onde ficavam o ícone/desinstalador do programa");
            }
            else if (match == NameMatch.Partial && !context.BelongsToAnotherApp(dirLeaf))
            {
                Add(dir, Confidence.Medium, "Pasta do ícone/desinstalador (nome parecido com o do programa)");
            }
            else if (IsDirectChildOfProgramRoot(dir, context))
            {
                Add(dir, Confidence.Medium, "Pasta do ícone/desinstalador em Arquivos de Programas");
            }
        }

        // 3) Cache do instalador (pacotes WiX/Burn guardam o setup em ProgramData\Package Cache\{GUID}).
        if (!string.IsNullOrWhiteSpace(app.BundleCachePath))
        {
            var dir = WinPath.GetParent(Environment.ExpandEnvironmentVariables(app.BundleCachePath));
            if (dir is not null && WinPath.IsStrictlyUnder(dir, context.Paths.PackageCache))
            {
                Add(dir, Confidence.High, "Cache do instalador deste programa (Package Cache)");
            }
        }

        return result;
    }

    private static bool IsDirectChildOfProgramRoot(string dir, ScanContext context)
    {
        var parent = WinPath.GetParent(dir);
        return parent is not null && context.Paths.ProgramRoots.Any(root => WinPath.AreEqual(root, parent));
    }
}
