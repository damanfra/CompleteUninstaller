using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Inventory;

public sealed record InventoryResult(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<string> Errors);

/// <summary>
/// Monta a lista de programas instalados a partir dos gerenciadores que existem na máquina
/// (dpkg, rpm, Flatpak, Snap) e junta os atalhos .desktop para mostrar nomes, ícones e esconder bibliotecas.
/// </summary>
public sealed class LinuxInventoryService
{
    private const int PathsPerQuery = 200;

    private readonly LinuxEnvironment _environment;

    public LinuxInventoryService(LinuxEnvironment environment) => _environment = environment;

    public Task<InventoryResult> LoadAsync() => Task.Run(Load);

    public InventoryResult Load()
    {
        var errors = new List<string>();
        var apps = new List<InstalledApp>();
        var any = false;

        if (CommandRunner.Exists("dpkg-query"))
        {
            any = true;
            apps.AddRange(LoadDpkg(errors));
        }

        if (CommandRunner.Exists("rpm"))
        {
            var rpm = LoadRpm(errors);
            any |= rpm.Count > 0;
            apps.AddRange(rpm);
        }

        if (CommandRunner.Exists("flatpak"))
        {
            any = true;
            apps.AddRange(LoadFlatpak(errors));
        }

        if (CommandRunner.Exists("snap"))
        {
            any = true;
            apps.AddRange(LoadSnap(errors));
        }

        if (!any)
        {
            errors.Add("Nenhum gerenciador de pacotes conhecido (dpkg, rpm, flatpak, snap) foi encontrado.");
        }

        AttachDesktopEntries(apps);
        var sorted = apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        Log.Info($"Inventário: {sorted.Count} itens ({string.Join(", ", sorted.GroupBy(a => a.Source).Select(g => $"{g.Key} {g.Count()}"))}).");
        return new InventoryResult(sorted, errors);
    }

    /// <summary>Confere se o pacote continua instalado (usado depois de rodar o desinstalador).</summary>
    public bool IsStillInstalled(InstalledApp app)
    {
        var name = app.PackageName ?? string.Empty;
        try
        {
            switch (app.Source)
            {
                case AppSource.Dpkg:
                    var dpkg = CommandRunner.Run("dpkg-query", ["-W", "-f=${db:Status-Abbrev}", name], timeoutMs: 30_000);
                    return dpkg.Success && dpkg.Output.TrimStart().StartsWith("ii", StringComparison.Ordinal);

                case AppSource.Rpm:
                    return CommandRunner.Run("rpm", ["-q", name], timeoutMs: 30_000).Success;

                case AppSource.Flatpak:
                    return CommandRunner.Run("flatpak", ["info", app.PackageScope == "user" ? "--user" : "--system", name],
                        timeoutMs: 30_000).Success;

                case AppSource.Snap:
                    return CommandRunner.Run("snap", ["list", name], timeoutMs: 30_000).Success;

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível verificar se '{app.DisplayName}' ainda está instalado: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lista o que o pacote instalou. Precisa ser lida ANTES de desinstalar. A lista inclui as pastas-pai
    /// compartilhadas (/usr, /etc...): serve só como candidatos; a posse é conferida na varredura.
    /// </summary>
    public IReadOnlyList<string> CapturePackageFiles(InstalledApp app)
    {
        var name = app.PackageName ?? string.Empty;
        CommandResult result = app.Source switch
        {
            AppSource.Dpkg => CommandRunner.Run("dpkg-query", ["-L", name], timeoutMs: 60_000),
            AppSource.Rpm => CommandRunner.Run("rpm", ["-ql", name], timeoutMs: 60_000),
            _ => new CommandResult(0, string.Empty, string.Empty),
        };

        var files = result.Success ? ToolOutputParsers.ParseFileList(result.Output) : [];
        if (app.InstallLocation is { Length: > 0 } location && app.Source is AppSource.Flatpak)
        {
            files = [.. files, location];
        }

        Log.Info($"Arquivos do pacote '{name}': {files.Count}.");
        return files;
    }

    private static List<InstalledApp> LoadDpkg(List<string> errors)
    {
        var query = CommandRunner.Run("dpkg-query", ["-W", $"-f={PackageParsers.DpkgQueryFormat}"], timeoutMs: 120_000);
        if (!query.Success && query.Output.Length == 0)
        {
            errors.Add($"dpkg-query falhou: {query.Error.Trim()}");
            return [];
        }

        HashSet<string>? manual = null;
        if (CommandRunner.Exists("apt-mark"))
        {
            var apt = CommandRunner.Run("apt-mark", ["showmanual"], timeoutMs: 60_000);
            if (apt.Success)
            {
                manual = ToolOutputParsers.ParseNameList(apt.Output);
            }
        }

        return PackageParsers.ParseDpkg(query.Output, manual).Installed;
    }

    private static List<InstalledApp> LoadRpm(List<string> errors)
    {
        var query = CommandRunner.Run("rpm", ["-qa", "--qf", PackageParsers.RpmQueryFormat], timeoutMs: 120_000);
        if (!query.Success && query.Output.Length == 0)
        {
            if (query.Error.Length > 0)
            {
                errors.Add($"rpm falhou: {query.Error.Trim()}");
            }

            return [];
        }

        return PackageParsers.ParseRpm(query.Output);
    }

    private List<InstalledApp> LoadFlatpak(List<string> errors)
    {
        const string columns = "--columns=application,name,version,branch,installation,origin,size";
        var apps = CommandRunner.Run("flatpak", ["list", "--app", columns], timeoutMs: 60_000);
        var runtimes = CommandRunner.Run("flatpak", ["list", "--runtime", columns], timeoutMs: 60_000);
        if (!apps.Success)
        {
            errors.Add($"flatpak falhou: {apps.Error.Trim()}");
            return [];
        }

        var result = PackageParsers.ParseFlatpak(apps.Output, _environment.Home, runtimes: false);
        if (runtimes.Success)
        {
            result.AddRange(PackageParsers.ParseFlatpak(runtimes.Output, _environment.Home, runtimes: true));
        }

        return result;
    }

    private static List<InstalledApp> LoadSnap(List<string> errors)
    {
        var list = CommandRunner.Run("snap", ["list"], timeoutMs: 60_000);
        if (!list.Success)
        {
            errors.Add($"snap falhou: {list.Error.Trim()}");
            return [];
        }

        var snaps = PackageParsers.ParseSnap(list.Output);
        foreach (var snap in snaps)
        {
            // O tamanho é o do arquivo comprimido (.snap) da revisão instalada.
            try
            {
                var file = new FileInfo($"/var/lib/snapd/snaps/{snap.PackageName}_{snap.PackageRevision}.snap");
                snap.EstimatedSizeBytes = file.Exists ? file.Length : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return snaps;
    }

    /// <summary>Liga cada pacote ao seu atalho .desktop: nome amigável, ícone e "é um aplicativo, não uma biblioteca".</summary>
    private void AttachDesktopEntries(List<InstalledApp> apps)
    {
        var entries = DesktopCatalog.Load(DesktopCatalog.ApplicationDirectories(_environment))
            .Where(e => !e.Hidden && e.Type is null or "Application")
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var byPackage = apps
            .Where(a => a.Source is AppSource.Dpkg or AppSource.Rpm)
            .GroupBy(a => ToolOutputParsers.StripArch(a.PackageName ?? string.Empty), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var owned = new List<(DesktopEntry Entry, string Package)>();
        var systemEntries = entries.Where(e => e.Path.StartsWith("/usr/share/applications/", StringComparison.Ordinal)).ToList();
        foreach (var chunk in systemEntries.Chunk(PackagesPerQueryChunk))
        {
            var paths = chunk.Select(e => e.Path).ToList();
            if (CommandRunner.Exists("dpkg-query"))
            {
                var result = CommandRunner.Run("dpkg-query", ["-S", .. paths], timeoutMs: 60_000);
                var map = ToolOutputParsers.ParseDpkgSearch(result.Output);
                foreach (var entry in chunk)
                {
                    if (map.TryGetValue(entry.Path, out var owners))
                    {
                        owned.AddRange(owners.Select(o => (entry, o)));
                    }
                }
            }
            else if (CommandRunner.Exists("rpm"))
            {
                var result = CommandRunner.Run("rpm", ["-qf", "--qf", "%{NAME}\\n", .. paths], timeoutMs: 60_000);
                var map = ToolOutputParsers.ParseRpmOwners(paths, result.Output);
                foreach (var entry in chunk)
                {
                    if (map.TryGetValue(entry.Path, out var owner))
                    {
                        owned.AddRange([(entry, owner)]);
                    }
                }
            }
        }

        foreach (var (entry, package) in owned)
        {
            if (byPackage.TryGetValue(package, out var matches))
            {
                matches.ForEach(app => Attach(app, entry));
            }
        }

        foreach (var app in apps.Where(a => a.Source is AppSource.Flatpak or AppSource.Snap))
        {
            var entry = app.Source == AppSource.Flatpak
                ? entries.FirstOrDefault(e => e.Stem == app.PackageName)
                : entries.FirstOrDefault(e => e.Path.StartsWith("/var/lib/snapd/desktop/applications/", StringComparison.Ordinal)
                                              && e.Stem.Split('_')[0] == app.PackageName);
            if (entry is not null)
            {
                Attach(app, entry);
            }
        }
    }

    private const int PackagesPerQueryChunk = PathsPerQuery;

    private static void Attach(InstalledApp app, DesktopEntry entry)
    {
        if (app.DesktopFile is not null)
        {
            return; // o primeiro atalho vence
        }

        app.DesktopFile = entry.Path;
        app.IconName = entry.Icon;
        app.Flags = (app.Flags | AppFlags.HasDesktopEntry) & ~AppFlags.Library;
        if (app.Source != AppSource.Flatpak)
        {
            // "Calculadora" em vez de "gnome-calculator"; o nome do pacote continua em ExtraNames.
            app.DisplayName = entry.Name;
        }

        app.ExtraNames.Add(entry.Stem);
        if (entry.ExecProgram is { } program && Path.GetFileName(program) is { Length: > 2 } leaf
            && leaf is not ("flatpak" or "env" or "snap" or "sh" or "bash"))
        {
            app.ExtraNames.Add(leaf);
        }
    }
}
