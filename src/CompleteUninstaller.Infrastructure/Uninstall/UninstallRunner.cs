using System.ComponentModel;
using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using Windows.Management.Deployment;

namespace CompleteUninstaller.Infrastructure.Uninstall;

public enum UninstallStatus
{
    Completed,
    Failed,
    Cancelled,
    NoUninstaller,
    StoppedWaiting,
}

public sealed record UninstallOutcome(UninstallStatus Status, int? ExitCode, string Message, bool RebootRequired = false);

/// <summary>Executa o desinstalador oficial do programa e espera ele (e os processos filhos) terminarem.</summary>
public sealed class UninstallRunner
{
    public async Task<UninstallOutcome> RunAsync(
        InstalledApp app,
        bool preferQuiet,
        IProgress<string>? progress,
        CancellationToken stopWaiting)
    {
        var plan = UninstallCommandBuilder.Build(app, preferQuiet, File.Exists);
        if (plan is null)
        {
            return new UninstallOutcome(UninstallStatus.NoUninstaller, null, "Este programa não informa um desinstalador.");
        }

        Log.Info($"Desinstalando '{app.DisplayName}' via {plan.Method}: {plan.CommandLine}");

        if (plan.Method == UninstallMethod.StorePackage)
        {
            return await RemovePackageAsync(plan.CommandLine, progress, stopWaiting);
        }

        progress?.Report($"Executando: {plan.CommandLine}");
        try
        {
            var workingDirectory = plan.WorkingDirectory is { } dir && Directory.Exists(dir) ? dir : null;
            using var tracked = TrackedProcess.Start(plan.CommandLine, workingDirectory);
            progress?.Report("Aguardando o desinstalador terminar...");

            try
            {
                while (tracked.ActiveProcessCount > 0)
                {
                    await Task.Delay(500, stopWaiting);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Info("Usuário parou de aguardar o desinstalador.");
                return new UninstallOutcome(UninstallStatus.StoppedWaiting, tracked.MainExitCode,
                    "Você indicou que o desinstalador já terminou.");
            }

            return Interpret(plan, tracked.MainExitCode);
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Falha ao iniciar o desinstalador de '{app.DisplayName}'", ex);
            return new UninstallOutcome(UninstallStatus.Failed, ex.NativeErrorCode,
                $"Não foi possível iniciar o desinstalador: {ex.Message}");
        }
    }

    private static UninstallOutcome Interpret(UninstallPlan plan, int? exitCode)
    {
        Log.Info($"Desinstalador terminou com código {exitCode?.ToString() ?? "desconhecido"}.");
        if (plan.Method == UninstallMethod.WindowsInstaller)
        {
            return exitCode switch
            {
                0 => new UninstallOutcome(UninstallStatus.Completed, 0, "Windows Installer concluiu a remoção."),
                3010 or 1641 => new UninstallOutcome(UninstallStatus.Completed, exitCode,
                    "Windows Installer concluiu a remoção; é preciso reiniciar o computador.", RebootRequired: true),
                1602 => new UninstallOutcome(UninstallStatus.Cancelled, exitCode, "A desinstalação foi cancelada."),
                1605 => new UninstallOutcome(UninstallStatus.Completed, exitCode,
                    "O Windows Installer informou que o produto já não estava instalado."),
                _ => new UninstallOutcome(UninstallStatus.Failed, exitCode,
                    $"O Windows Installer terminou com o código {exitCode}."),
            };
        }

        // Códigos de saída de desinstaladores comuns não seguem padrão; o resultado real é
        // conferido depois, verificando se o programa ainda está registrado.
        return new UninstallOutcome(UninstallStatus.Completed, exitCode,
            $"O desinstalador terminou (código {exitCode?.ToString() ?? "?"}).");
    }

    private static async Task<UninstallOutcome> RemovePackageAsync(
        string packageFullName,
        IProgress<string>? progress,
        CancellationToken stopWaiting)
    {
        try
        {
            var manager = new PackageManager();

            // Pacote já removido por outro programa (ex.: app Xbox): não há o que desinstalar.
            var package = manager.FindPackageForUser(string.Empty, packageFullName);
            if (package is null)
            {
                Log.Info($"Pacote {packageFullName} não está mais registrado para este usuário.");
                return new UninstallOutcome(UninstallStatus.Completed, null, "O pacote já não estava instalado.");
            }

            LogPackageStatus(package);
            progress?.Report($"Removendo o pacote {packageFullName}...");

            // O Windows executa uma operação de implantação por vez: se outra estiver em andamento
            // (atualização da Store, Gaming Services), esta espera na fila sem dar sinal.
            var lastReported = -1;
            var deploymentProgress = new Progress<DeploymentProgress>(p =>
            {
                var step = (int)p.percentage / 10 * 10;
                if (step > lastReported)
                {
                    lastReported = step;
                    progress?.Report($"Remoção do pacote: {step}%");
                }
            });

            var operation = manager.RemovePackageAsync(packageFullName);
            var removal = operation.AsTask(deploymentProgress);
            var stop = Task.Delay(Timeout.Infinite, stopWaiting);
            if (await Task.WhenAny(removal, stop) != removal)
            {
                // Não espera o Cancel ser aceito: alguns pacotes (jogos) ignoram o pedido.
                try
                {
                    operation.Cancel();
                }
                catch (Exception ex)
                {
                    Log.Warn($"Não foi possível cancelar a remoção de {packageFullName}: {ex.Message}");
                }

                Log.Info($"Usuário parou de aguardar a remoção do pacote {packageFullName}.");
                return new UninstallOutcome(UninstallStatus.StoppedWaiting, null,
                    "Você parou de aguardar a remoção do pacote. O Windows pode continuar a remoção em segundo plano.");
            }

            var result = await removal;
            if (result.ExtendedErrorCode is { } error)
            {
                Log.Warn($"Remoção do pacote {packageFullName} falhou: 0x{error.HResult:X8} {result.ErrorText}");
                return new UninstallOutcome(UninstallStatus.Failed, error.HResult, result.ErrorText);
            }

            return new UninstallOutcome(UninstallStatus.Completed, 0, "Pacote removido.");
        }
        catch (Exception ex)
        {
            Log.Error($"Falha removendo o pacote {packageFullName}", ex);
            return new UninstallOutcome(UninstallStatus.Failed, ex.HResult, $"Falha ao remover o pacote: {ex.Message}");
        }
    }

    /// <summary>Registra no log o estado do pacote (ajuda a entender remoções que travam).</summary>
    private static void LogPackageStatus(Windows.ApplicationModel.Package package)
    {
        try
        {
            var status = package.Status;
            Log.Info(
                $"Estado do pacote {package.Id.FullName}: OK={status.VerifyIsOK()}, NotAvailable={status.NotAvailable}, " +
                $"Modified={status.Modified}, Tampered={status.Tampered}, Disabled={status.Disabled}, " +
                $"DeploymentInProgress={status.DeploymentInProgress}, Servicing={status.Servicing}, " +
                $"Local={SafePath(package)}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível ler o estado do pacote: {ex.Message}");
        }
    }

    private static string SafePath(Windows.ApplicationModel.Package package)
    {
        try
        {
            return package.InstalledPath ?? "?";
        }
        catch (Exception ex)
        {
            return $"inacessível ({ex.Message})";
        }
    }
}
