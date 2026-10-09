using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Infrastructure.Native;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>Atalhos e pastas do Menu Iniciar e da Área de Trabalho (de todos os perfis).</summary>
internal static class ShortcutScanner
{
    public static void Scan(ScanContext context, LeftoverCollector collector)
    {
        var paths = context.Paths;
        var menuRoots = new[] { paths.CommonStartMenuPrograms }
            .Concat(paths.Profiles.Select(p => p.StartMenuPrograms))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in menuRoots)
        {
            foreach (var dir in FileSystemHelper.GetDirectories(root))
            {
                var leaf = WinPath.GetLeaf(dir);
                var match = context.Matcher.Match(leaf);
                if (match == NameMatch.Exact && !context.BelongsToAnotherApp(leaf))
                {
                    context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.High,
                        "Pasta do programa no Menu Iniciar");
                }
                else if (match == NameMatch.Partial && !context.BelongsToAnotherApp(leaf))
                {
                    context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.Medium,
                        "Pasta no Menu Iniciar com nome parecido com o do programa");
                }
            }

            foreach (var shortcut in FileSystemHelper.GetFilesRecursive(root, "*.lnk", maxDepth: 3))
            {
                Evaluate(context, collector, shortcut, "Menu Iniciar");
            }
        }

        var desktops = new[] { paths.CommonDesktop, paths.CurrentUserDesktop }
            .Concat(paths.Profiles.Select(p => p.Desktop))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var desktop in desktops)
        {
            foreach (var shortcut in FileSystemHelper.GetFiles(desktop, "*.lnk"))
            {
                Evaluate(context, collector, shortcut, "Área de Trabalho");
            }
        }
    }

    private static void Evaluate(ScanContext context, LeftoverCollector collector, string shortcut, string label)
    {
        var target = ShellLinkReader.TryGetTarget(shortcut);

        // 1) Aponta para a pasta do programa: ligação comprovada.
        if (context.FindInstallDir(target) is { } dir)
        {
            context.TryAddPath(collector, LeftoverKind.Shortcut, shortcut, dir.Confidence,
                $"Atalho ({label}) aponta para a pasta do programa");
            return;
        }

        // 2) Atalho "anunciado" do Windows Installer (destino em C:\Windows\Installer\{ProductCode}).
        if (target is not null && context.Target.MsiProductCode is { } code
            && target.Contains(code, StringComparison.OrdinalIgnoreCase))
        {
            context.TryAddPath(collector, LeftoverKind.Shortcut, shortcut, Confidence.High,
                $"Atalho do Windows Installer ({label}) deste produto");
            return;
        }

        // 3) Pelo nome: só se o destino não existir mais (atalho quebrado).
        var name = Path.GetFileNameWithoutExtension(shortcut);
        var match = context.Matcher.Match(name);
        if (match is not (NameMatch.Exact or NameMatch.Partial) || context.BelongsToAnotherApp(name))
        {
            return;
        }

        if (target is not null && !FileSystemHelper.Exists(target))
        {
            context.TryAddPath(collector, LeftoverKind.Shortcut, shortcut,
                match == NameMatch.Exact ? Confidence.High : Confidence.Medium,
                $"Atalho quebrado ({label}) com o nome do programa");
        }
        else if (target is null && match == NameMatch.Exact)
        {
            context.TryAddPath(collector, LeftoverKind.Shortcut, shortcut, Confidence.Medium,
                $"Atalho ({label}) com o nome do programa e destino ilegível");
        }
    }
}
