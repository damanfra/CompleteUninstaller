using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Core.Text;
using CompleteUninstaller.Infrastructure.Linux.Inventory;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Leftovers;

/// <summary>
/// Procura sobras de um programa já desinstalado (nível Seguro ou Moderado): pastas de configuração, dados e
/// cache na pasta pessoal, restos em /opt, /etc, /var/lib..., atalhos .desktop e unidades do systemd.
/// Tudo passa pelo <see cref="PathGuard"/>, inclusive a conferência de posse por pacote instalado.
/// </summary>
public sealed class LinuxLeftoverScanner
{
    private static readonly string[] ConfigLeftoverSuffixes = [".dpkg-old", ".dpkg-dist", ".dpkg-new", ".rpmsave", ".rpmnew", ".rpmorig"];

    private static readonly string[] ConfigExtensions = [".conf", ".cfg", ".ini", ".json", ".yaml", ".yml", ".toml", ".rc"];

    private static readonly string[] SystemFolderRoots =
    [
        "/opt", "/etc", "/srv", "/usr/share", "/usr/lib", "/usr/libexec", "/usr/local/share", "/usr/local/lib",
        "/usr/local/etc", "/var/lib", "/var/cache", "/var/log", "/var/snap",
    ];

    private readonly LinuxEnvironment _environment;

    public LinuxLeftoverScanner(LinuxEnvironment environment) => _environment = environment;

    /// <summary>A proteção completa: áreas do sistema, pasta pessoal, outros programas e pacotes instalados.</summary>
    public PathGuard CreateGuard(InstalledApp target, IReadOnlyList<InstalledApp> inventory)
    {
        var others = inventory.Where(a => !IsSameApp(a, target)).ToList();
        var ownership = new PackageOwnership(
            inventory.Where(a => a.Source is AppSource.Dpkg or AppSource.Rpm).Select(a => a.PackageName!));
        return UnixSafetyRules.CreateGuard(
            _environment.Profiles,
            _environment.OwnTrees(),
            others.SelectMany(KnownDirectories),
            ownership.OwnerOf);
    }

    public IReadOnlyList<LeftoverItem> Scan(
        InstalledApp target,
        CleanupLevel level,
        IReadOnlyList<InstalledApp> currentInventory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Log.Info($"Procurando sobras de '{target.DisplayName}' (nível {level}).");
        var context = ScanContext.Create(this, target, currentInventory);
        var collector = new LeftoverCollector(UnixPathRules.Instance);

        progress?.Report("Conferindo o que o pacote instalou e ainda está no disco...");
        ScanOwnedPaths(context, collector, cancellationToken);

        progress?.Report("Procurando pastas de configuração, dados e cache...");
        ScanFolders(context, collector, cancellationToken);
        ScanPackageSpecific(context, collector);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Procurando atalhos e programas de inicialização automática...");
        ScanDesktopFiles(context, collector);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Procurando serviços do systemd...");
        ScanSystemdUnits(context, collector);

        var items = collector.Build(level);

        progress?.Report("Calculando tamanhos...");
        foreach (var item in items.Where(i => i.IsFileSystem))
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.SizeBytes = FileSystemHelper.GetSize(item.Target);
        }

        foreach (var item in items)
        {
            Log.Info($"Sobra [{item.Confidence}] {item.Kind}: {item.Target} — {item.Reason}");
        }

        Log.Info($"{items.Count} sobras encontradas para '{target.DisplayName}'.");
        return items;
    }

    // ---------- arquivos que o pacote declarou ----------

    /// <summary>
    /// O que o pacote instalou e continua no disco (por exemplo, arquivos de configuração que o apt mantém).
    /// A lista inclui pastas compartilhadas; quem decide é a proteção de caminhos (posse por outro pacote).
    /// </summary>
    private static void ScanOwnedPaths(ScanContext context, LeftoverCollector collector, CancellationToken cancellationToken)
    {
        if (context.Target.OwnedPaths is not { Count: > 0 } owned)
        {
            return;
        }

        var existing = owned
            .Where(p => context.StaticGuard.CanRemove(p) && FileSystemHelper.Exists(p))
            .OrderBy(p => p.Count(c => c == '/'))
            .Take(500)
            .ToList();
        foreach (var path in existing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = Directory.Exists(path) ? LeftoverKind.Folder : LeftoverKind.File;
            context.TryAddPath(collector, kind, path, Confidence.High, "Arquivo do pacote que continua no disco após a remoção");
        }
    }

    // ---------- pastas por nome ----------

    private void ScanFolders(ScanContext context, LeftoverCollector collector, CancellationToken cancellationToken)
    {
        foreach (var (root, label, dotOnly) in GetRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var dir in FileSystemHelper.GetDirectories(root))
            {
                var leaf = UnixPath.GetLeaf(dir);
                if (dotOnly && !leaf.StartsWith('.'))
                {
                    continue;
                }

                var name = leaf.TrimStart('.');
                switch (context.Matcher.Match(name))
                {
                    case NameMatch.Exact when !context.BelongsToAnotherApp(name):
                        context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.High,
                            $"Pasta com o nome do programa em {label}");
                        break;

                    case NameMatch.Partial when !context.BelongsToAnotherApp(name):
                        context.TryAddPath(collector, LeftoverKind.Folder, dir, Confidence.Medium,
                            $"Pasta com nome parecido com o do programa em {label}");
                        break;
                }
            }
        }

        // Arquivos soltos de configuração em /etc (foo.conf) e restos de atualização de configuração (foo.conf.rpmsave).
        foreach (var file in FileSystemHelper.GetFiles("/etc"))
        {
            var leaf = UnixPath.GetLeaf(file);
            var suffix = ConfigLeftoverSuffixes.FirstOrDefault(s => leaf.EndsWith(s, StringComparison.Ordinal));
            var stemSource = suffix is null ? leaf : leaf[..^suffix.Length];
            var extension = ConfigExtensions.FirstOrDefault(e => stemSource.EndsWith(e, StringComparison.Ordinal));
            if (suffix is null && extension is null)
            {
                continue;
            }

            var stem = extension is null ? stemSource : stemSource[..^extension.Length];
            if (context.Matcher.Match(stem) == NameMatch.Exact && !context.BelongsToAnotherApp(stem))
            {
                context.TryAddPath(collector, LeftoverKind.File, file,
                    suffix is null ? Confidence.Medium : Confidence.High,
                    suffix is null ? "Arquivo de configuração com o nome do programa em /etc" : "Cópia de configuração deixada pelo gerenciador de pacotes");
            }
        }
    }

    /// <summary>Dados de Flatpak (~/.var/app/ID) e de Snap (~/snap/NOME e /var/snap/NOME): ligação comprovada pelo ID.</summary>
    private void ScanPackageSpecific(ScanContext context, LeftoverCollector collector)
    {
        var target = context.Target;
        var name = target.PackageName;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        switch (target.Source)
        {
            case AppSource.Flatpak:
                AddIfExists(context, collector, UnixPath.Combine(_environment.Home, ".var/app/" + name),
                    "Dados do aplicativo Flatpak (~/.var/app)");
                break;

            case AppSource.Snap:
                AddIfExists(context, collector, UnixPath.Combine(_environment.Home, "snap/" + name), "Dados do Snap (~/snap)");
                AddIfExists(context, collector, "/var/snap/" + name, "Dados do sistema do Snap (/var/snap)");
                break;
        }
    }

    private static void AddIfExists(ScanContext context, LeftoverCollector collector, string path, string reason)
    {
        if (Directory.Exists(path))
        {
            context.TryAddPath(collector, LeftoverKind.Folder, path, Confidence.High, reason);
        }
    }

    // ---------- atalhos ----------

    private void ScanDesktopFiles(ScanContext context, LeftoverCollector collector)
    {
        var directories = DesktopCatalog.ApplicationDirectories(_environment)
            .Concat(DesktopCatalog.AutostartDirectories(_environment));
        foreach (var entry in DesktopCatalog.Load(directories))
        {
            var byStem = context.Matcher.Match(entry.Stem.Split('_')[0]);
            var byName = context.Matcher.Match(entry.Name);
            var match = byStem == NameMatch.Exact || byName == NameMatch.Exact ? NameMatch.Exact
                : byStem == NameMatch.Partial || byName == NameMatch.Partial ? NameMatch.Partial
                : NameMatch.None;
            var broken = context.IsBrokenProgram(entry.ExecProgram);
            var belongsToTarget = context.IsOwnedProgram(entry.ExecProgram);

            if (belongsToTarget)
            {
                context.TryAddPath(collector, LeftoverKind.Shortcut, entry.Path, Confidence.High,
                    "Atalho que executa um arquivo do programa");
            }
            else if (match == NameMatch.Exact && broken && !context.BelongsToAnotherApp(entry.Stem))
            {
                context.TryAddPath(collector, LeftoverKind.Shortcut, entry.Path, Confidence.High,
                    "Atalho com o nome do programa e destino inexistente");
            }
            else if (match == NameMatch.Partial && broken && !context.BelongsToAnotherApp(entry.Stem))
            {
                context.TryAddPath(collector, LeftoverKind.Shortcut, entry.Path, Confidence.Medium,
                    "Atalho com nome parecido com o do programa e destino inexistente");
            }
        }
    }

    // ---------- systemd ----------

    private void ScanSystemdUnits(ScanContext context, LeftoverCollector collector)
    {
        // Só unidades criadas pelo administrador ou pelo usuário: as dos pacotes (/usr/lib/systemd) o gerenciador já removeu.
        var directories = new[] { "/etc/systemd/system", UnixPath.Combine(_environment.Home, ".config/systemd/user") };
        foreach (var dir in directories)
        {
            foreach (var unit in FileSystemHelper.GetFiles(dir).Where(f => f.EndsWith(".service", StringComparison.Ordinal) || f.EndsWith(".timer", StringComparison.Ordinal)))
            {
                if (FileSystemHelper.IsLink(unit))
                {
                    continue; // links são unidades habilitadas ou apelidos; não mexemos
                }

                var leaf = UnixPath.GetLeaf(unit);
                var stem = leaf[..leaf.LastIndexOf('.')];
                var exec = ReadExecStart(unit);
                var match = context.Matcher.Match(stem);
                if (context.IsOwnedProgram(exec))
                {
                    AddUnit(context, collector, unit, exec, Confidence.High, "Serviço que executa um arquivo do programa");
                }
                else if (match == NameMatch.Exact && context.IsBrokenProgram(exec) && !context.BelongsToAnotherApp(stem))
                {
                    AddUnit(context, collector, unit, exec, Confidence.High, "Serviço com o nome do programa e executável inexistente");
                }
                else if (match == NameMatch.Partial && !context.BelongsToAnotherApp(stem))
                {
                    AddUnit(context, collector, unit, exec, Confidence.Medium, "Serviço com nome parecido com o do programa");
                }
            }
        }
    }

    private static void AddUnit(ScanContext context, LeftoverCollector collector, string unit, string? exec, Confidence confidence, string reason)
    {
        if (!context.Guard.CanRemove(unit, out var why))
        {
            Log.Info($"Protegido, ignorado: {unit} — {why}");
            return;
        }

        collector.Add(new LeftoverItem
        {
            Kind = LeftoverKind.Service,
            Target = unit,
            Confidence = confidence,
            Reason = reason,
            Details = exec,
            SizeBytes = FileSystemHelper.GetSize(unit),
        });
    }

    private static string? ReadExecStart(string unitFile)
    {
        try
        {
            var line = File.ReadLines(unitFile).FirstOrDefault(l => l.TrimStart().StartsWith("ExecStart=", StringComparison.Ordinal));
            if (line is null)
            {
                return null;
            }

            var command = line.Trim()["ExecStart=".Length..].TrimStart('@', '-', ':', '+', '!');
            return DesktopEntry.ExtractProgram(command);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---------- raízes ----------

    private IEnumerable<(string Root, string Label, bool DotOnly)> GetRoots()
    {
        var home = _environment.Home;
        yield return (home, "a pasta pessoal", true);
        foreach (var relative in new[] { ".config", ".local/share", ".local/state", ".cache", ".local/lib", ".var/app", "snap" })
        {
            yield return (UnixPath.Combine(home, relative), "~/" + relative, false);
        }

        foreach (var root in SystemFolderRoots)
        {
            yield return (root, root, false);
        }
    }

    private static bool IsSameApp(InstalledApp a, InstalledApp target)
    {
        if (a.Id == target.Id)
        {
            return true;
        }

        if (a.PackageName is not null && a.Source == target.Source && a.PackageName == target.PackageName
            && a.PackageScope == target.PackageScope)
        {
            return true;
        }

        return NameNormalizer.Normalize(a.DisplayName) == NameNormalizer.Normalize(target.DisplayName)
            && string.Equals(a.DisplayVersion, target.DisplayVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pastas que sabidamente pertencem a um programa instalado (instalação e executável do atalho).</summary>
    private static IEnumerable<string> KnownDirectories(InstalledApp app)
    {
        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            yield return app.InstallLocation;
        }

        if (app.Source is AppSource.Flatpak && app.PackageName is { } id)
        {
            yield return "/var/lib/flatpak/app/" + id;
        }
    }

    private sealed class ScanContext
    {
        private readonly HashSet<string> _ownedExact;
        private readonly List<string> _ownedDirectories;

        private ScanContext(
            InstalledApp target,
            AppNameMatcher matcher,
            IReadOnlyList<AppNameMatcher> others,
            PathGuard guard,
            PathGuard staticGuard)
        {
            Target = target;
            Matcher = matcher;
            OtherAppMatchers = others;
            Guard = guard;
            StaticGuard = staticGuard;

            var owned = target.OwnedPaths ?? [];
            _ownedExact = owned.ToHashSet(StringComparer.Ordinal);
            // Pastas do pacote só valem como "ligação comprovada" se forem específicas dele: /usr/share/applications,
            // /usr/lib/python3 e afins aparecem na lista de qualquer pacote e estão protegidas pela proteção estática.
            _ownedDirectories = owned
                .Where(p => StaticGuard.CanRemove(p) && !AppNameMatcher.IsGeneric(UnixPath.GetLeaf(p)))
                .Where(p => UnixSafetyRules.GenericNames.All(g => UnixPath.GetLeaf(p) != g))
                .ToList();
        }

        public InstalledApp Target { get; }

        public AppNameMatcher Matcher { get; }

        public IReadOnlyList<AppNameMatcher> OtherAppMatchers { get; }

        /// <summary>Proteção completa (inclui pacote dono).</summary>
        public PathGuard Guard { get; }

        /// <summary>Proteção só pelas regras de caminho, sem consultar o gerenciador de pacotes.</summary>
        public PathGuard StaticGuard { get; }

        public static ScanContext Create(LinuxLeftoverScanner scanner, InstalledApp target, IReadOnlyList<InstalledApp> inventory)
        {
            var alternatives = new List<string>(target.ExtraNames);
            if (target.PackageName is { Length: > 0 } package)
            {
                alternatives.Add(ToolOutputParsers.StripArch(package));
            }

            var matcher = new AppNameMatcher(target.DisplayName, null, alternatives, UnixSafetyRules.GenericNames);
            var others = inventory
                .Where(a => !IsSameApp(a, target) && !a.IsLibrary && !a.IsUpdate)
                .Select(a => new AppNameMatcher(a.DisplayName, null, a.ExtraNames, UnixSafetyRules.GenericNames))
                .ToList();

            var guard = scanner.CreateGuard(target, inventory);
            var staticGuard = UnixSafetyRules.CreateGuard(scanner._environment.Profiles, scanner._environment.OwnTrees(), [], null);
            return new ScanContext(target, matcher, others, guard, staticGuard);
        }

        public bool BelongsToAnotherApp(string name) =>
            OtherAppMatchers.Any(m => m.Match(name) == NameMatch.Exact);

        /// <summary>O executável é um arquivo que o pacote instalou (ou está numa pasta específica dele).</summary>
        public bool IsOwnedProgram(string? program)
        {
            if (string.IsNullOrEmpty(program) || !program.StartsWith('/'))
            {
                return false;
            }

            return _ownedExact.Contains(program)
                || _ownedDirectories.Any(d => UnixPath.IsStrictlyUnder(program, d));
        }

        /// <summary>O executável não existe mais (caminho absoluto ausente ou comando fora do PATH).</summary>
        public bool IsBrokenProgram(string? program)
        {
            if (string.IsNullOrEmpty(program))
            {
                return false;
            }

            return program.StartsWith('/') ? !File.Exists(program) : CommandRunner.Find(program) is null;
        }

        /// <summary>Adiciona um item de arquivo/pasta passando pela proteção de caminhos.</summary>
        public bool TryAddPath(LeftoverCollector collector, LeftoverKind kind, string path, Confidence confidence, string reason)
        {
            if (!Guard.CanRemove(path, out var why))
            {
                Log.Info($"Protegido, ignorado: {path} — {why}");
                return false;
            }

            if (FileSystemHelper.IsLink(path))
            {
                Log.Info($"Link simbólico, ignorado: {path}");
                return false;
            }

            collector.Add(new LeftoverItem { Kind = kind, Target = path, Confidence = confidence, Reason = reason });
            return true;
        }
    }
}
