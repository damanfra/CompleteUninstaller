using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Uninstall;

public enum UninstallStatus
{
    Completed,
    Failed,
    Cancelled,
    NoUninstaller,
}

public sealed record UninstallOutcome(UninstallStatus Status, int? ExitCode, string Message, bool RebootRequired = false);

/// <summary>Resultado da simulação: o que mais seria removido junto com o programa.</summary>
public sealed record UninstallPreview(string? Error, IReadOnlyList<string> OtherPackages);

/// <summary>Remove o pacote com o gerenciador certo (apt, dnf, flatpak, snap), pedindo senha pelo pkexec quando preciso.</summary>
public sealed class LinuxUninstallRunner
{
    private readonly LinuxEnvironment _environment;

    public LinuxUninstallRunner(LinuxEnvironment environment) => _environment = environment;

    /// <summary>
    /// apt e dnf removem também os pacotes que dependem do alvo. A simulação mostra isso antes de qualquer alteração.
    /// </summary>
    public Task<UninstallPreview> PreviewAsync(InstalledApp app) => Task.Run(() =>
    {
        var plan = LinuxUninstallCommands.Build(app);
        if (plan.Error is not null)
        {
            return new UninstallPreview(plan.Error, []);
        }

        if (plan.Simulate is null)
        {
            return new UninstallPreview(null, []);
        }

        var (file, args, env) = CommandRunner.Elevate(plan.Simulate, _environment.IsRoot);
        Log.Info($"Simulando a remoção de '{app.DisplayName}': {file} {string.Join(' ', args)}");
        var result = CommandRunner.Run(file, args, env, timeoutMs: 180_000);
        if (IsAuthenticationRefused(plan.Simulate, result.ExitCode))
        {
            return new UninstallPreview("A autenticação foi cancelada.", []);
        }

        if (!LinuxUninstallCommands.SimulationSucceeded(app, result.ExitCode, result.Output))
        {
            var message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            return new UninstallPreview($"A simulação da remoção falhou: {message.Trim()}", []);
        }

        var others = LinuxUninstallCommands.OtherPackagesInSimulation(app, result.Output);
        Log.Info($"Simulação: {others.Count} outro(s) pacote(s) seriam removidos: {string.Join(", ", others)}");
        return new UninstallPreview(null, others);
    });

    public async Task<UninstallOutcome> RunAsync(InstalledApp app, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var plan = LinuxUninstallCommands.Build(app);
        if (plan.Remove is null)
        {
            return new UninstallOutcome(UninstallStatus.NoUninstaller, null, plan.Error ?? "Este programa não pode ser removido.");
        }

        var (file, args, env) = CommandRunner.Elevate(plan.Remove, _environment.IsRoot);
        Log.Info($"Desinstalando '{app.DisplayName}': {file} {string.Join(' ', args)}");
        progress?.Report($"Executando: {plan.Remove}");

        var exitCode = await CommandRunner.RunStreamingAsync(file, args, env, line => progress?.Report(line), cancellationToken);
        Log.Info($"Gerenciador de pacotes terminou com código {exitCode}.");

        if (IsAuthenticationRefused(plan.Remove, exitCode))
        {
            return new UninstallOutcome(UninstallStatus.Cancelled, exitCode, "A autenticação foi cancelada; nada foi removido.");
        }

        if (exitCode != 0)
        {
            return new UninstallOutcome(UninstallStatus.Failed, exitCode,
                $"O gerenciador de pacotes terminou com o código {exitCode}. Veja as mensagens acima.");
        }

        var message = app.Source == AppSource.Snap
            ? "Snap removido. Se o Snap guardou um instantâneo automático dos dados, ele aparece em \"snap saved\"."
            : "O gerenciador de pacotes concluiu a remoção.";
        return new UninstallOutcome(UninstallStatus.Completed, 0, message);
    }

    /// <summary>pkexec devolve 126 quando a janela de senha é fechada e 127 quando o usuário não tem autorização.</summary>
    private bool IsAuthenticationRefused(LinuxCommand command, int exitCode) =>
        command.NeedsRoot && !_environment.IsRoot && exitCode is 126 or 127;
}
