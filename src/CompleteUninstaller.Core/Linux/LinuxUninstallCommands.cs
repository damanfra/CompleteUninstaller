using System.Text.RegularExpressions;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.Core.Linux;

/// <summary>Um comando a executar (sem shell: o nome do programa e cada argumento vão separados).</summary>
public sealed record LinuxCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool NeedsRoot,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    public override string ToString() => string.Join(' ', new[] { FileName }.Concat(Arguments));
}

public sealed record LinuxUninstallPlan(LinuxCommand? Remove, LinuxCommand? Simulate, string? Error);

/// <summary>
/// Monta os comandos de remoção de cada gerenciador. Regras: nunca "purge" nem "--delete-data" (os dados
/// vão para a quarentena depois, não somem); nunca passa pelo shell; nomes de pacote são validados para
/// que nada que comece com "-" vire opção do comando.
/// </summary>
public static partial class LinuxUninstallCommands
{
    [GeneratedRegex(@"^[a-z0-9][a-z0-9+.\-]*(:[a-z0-9\-]+)?$")]
    private static partial Regex DpkgName();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_+.\-]*$")]
    private static partial Regex RpmName();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.\-]*$")]
    private static partial Regex FlatpakName();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]*$")]
    private static partial Regex SnapName();

    public static LinuxUninstallPlan Build(InstalledApp app)
    {
        if (app.Flags.HasFlag(AppFlags.NoRemove))
        {
            return new LinuxUninstallPlan(null, null, "Este pacote é essencial para o sistema e não pode ser removido por aqui.");
        }

        var name = app.PackageName ?? string.Empty;
        switch (app.Source)
        {
            case AppSource.Dpkg when DpkgName().IsMatch(name):
                var apt = new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };
                return new LinuxUninstallPlan(
                    new LinuxCommand("apt-get", ["remove", "-y", name], NeedsRoot: true, apt),
                    new LinuxCommand("apt-get", ["-s", "remove", name], NeedsRoot: false),
                    null);

            case AppSource.Rpm when RpmName().IsMatch(name):
                return new LinuxUninstallPlan(
                    new LinuxCommand("dnf", ["remove", "-y", name], NeedsRoot: true),
                    new LinuxCommand("dnf", ["remove", "--assumeno", name], NeedsRoot: true),
                    null);

            case AppSource.Flatpak when FlatpakName().IsMatch(name)
                                        && app.PackageScope is "user" or "system"
                                        && FlatpakName().IsMatch(app.PackageBranch ?? "stable"):
                var scope = app.PackageScope == "user" ? "--user" : "--system";
                return new LinuxUninstallPlan(
                    new LinuxCommand("flatpak", ["uninstall", scope, "-y", "--noninteractive", $"{name}//{app.PackageBranch ?? "stable"}"],
                        NeedsRoot: false),
                    null,
                    null);

            case AppSource.Snap when SnapName().IsMatch(name):
                return new LinuxUninstallPlan(new LinuxCommand("snap", ["remove", name], NeedsRoot: true), null, null);

            default:
                return new LinuxUninstallPlan(null, null, "Nome de pacote inválido ou origem sem suporte.");
        }
    }

    /// <summary>Pacotes que seriam removidos junto com o alvo (dependentes), sem contar o próprio alvo.</summary>
    public static List<string> OtherPackagesInSimulation(InstalledApp app, string simulationOutput)
    {
        var removed = app.Source switch
        {
            AppSource.Dpkg => ToolOutputParsers.ParseAptSimulation(simulationOutput),
            AppSource.Rpm => ToolOutputParsers.ParseDnfSimulation(simulationOutput),
            _ => [],
        };

        var target = ToolOutputParsers.StripArch(app.PackageName ?? string.Empty);
        return removed
            .Where(r => !string.Equals(ToolOutputParsers.StripArch(r), target, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
