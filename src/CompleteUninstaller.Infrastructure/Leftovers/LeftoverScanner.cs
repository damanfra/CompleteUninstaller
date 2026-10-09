using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>
/// Procura sobras de um programa já desinstalado (nível Seguro ou Moderado).
/// Nesta fase cobre arquivos/pastas, atalhos, serviços e tarefas agendadas — o Registro fica para a fase 2.
/// </summary>
public sealed class LeftoverScanner
{
    private readonly SystemPaths _paths;

    public LeftoverScanner(SystemPaths paths) => _paths = paths;

    /// <param name="target">O programa (dados capturados ANTES da desinstalação).</param>
    /// <param name="level">Nível de limpeza.</param>
    /// <param name="currentInventory">Inventário lido DEPOIS da desinstalação (o que continua instalado é protegido).</param>
    /// <param name="progress">Mensagens de andamento.</param>
    /// <param name="cancellationToken">Cancelamento.</param>
    public IReadOnlyList<LeftoverItem> Scan(
        InstalledApp target,
        CleanupLevel level,
        IReadOnlyList<InstalledApp> currentInventory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Log.Info($"Procurando sobras de '{target.DisplayName}' (nível {level}).");
        var context = ScanContext.Create(target, currentInventory, _paths);
        var collector = new LeftoverCollector();

        progress?.Report("Procurando pastas de programa, dados e configuração...");
        FolderScanner.Scan(context, collector, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Procurando atalhos no Menu Iniciar e na Área de Trabalho...");
        ShortcutScanner.Scan(context, collector);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Procurando serviços...");
        ServiceScanner.Scan(context, collector);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Procurando tarefas agendadas...");
        ScheduledTaskScanner.Scan(context, collector);

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
}
