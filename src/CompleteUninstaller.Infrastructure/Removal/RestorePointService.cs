using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Removal;

public enum RestorePointStatus
{
    Created,

    /// <summary>O Windows não criou (por padrão, só permite um ponto a cada 24 horas).</summary>
    Skipped,

    Failed,
}

public sealed record RestorePointResult(RestorePointStatus Status, string Message);

/// <summary>Cria um ponto de restauração do sistema via Checkpoint-Computer (fora do processo, mais robusto).</summary>
public sealed class RestorePointService
{
    public Task<RestorePointResult> CreateAsync(string description) => Task.Run(() => Create(description));

    private static RestorePointResult Create(string description)
    {
        var safe = description.Replace("'", "''", StringComparison.Ordinal);
        if (safe.Length > 200)
        {
            safe = safe[..200];
        }

        // Compara o maior número de sequência antes e depois para saber se o ponto foi realmente criado.
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            "try { " +
            "$before = (Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Measure-Object -Property SequenceNumber -Maximum).Maximum; " +
            $"Checkpoint-Computer -Description '{safe}' -RestorePointType APPLICATION_UNINSTALL -WarningAction SilentlyContinue; " +
            "$after = (Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Measure-Object -Property SequenceNumber -Maximum).Maximum; " +
            "if ($after -gt $before) { exit 0 } else { exit 2 } " +
            "} catch { Write-Output $_.Exception.Message; exit 1 }";

        try
        {
            var result = ProcessRunner.Run(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
                timeoutMs: 300_000);

            Log.Info($"Ponto de restauração: código {result.ExitCode}. {result.Output}");
            return result.ExitCode switch
            {
                0 => new RestorePointResult(RestorePointStatus.Created, "Ponto de restauração criado."),
                2 => new RestorePointResult(RestorePointStatus.Skipped,
                    "O Windows não criou um novo ponto de restauração (normalmente porque já existe um das últimas 24 horas)."),
                _ => new RestorePointResult(RestorePointStatus.Failed,
                    $"Não foi possível criar o ponto de restauração. Verifique se a Proteção do Sistema está ativada na unidade do Windows. {result.Output}".Trim()),
            };
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao criar ponto de restauração", ex);
            return new RestorePointResult(RestorePointStatus.Failed, $"Não foi possível criar o ponto de restauração: {ex.Message}");
        }
    }
}
