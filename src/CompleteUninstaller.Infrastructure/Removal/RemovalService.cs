using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Removal;

/// <summary>
/// Remove as sobras selecionadas pelo usuário. Nada é apagado direto: arquivos vão para a quarentena,
/// e serviços/tarefas têm a configuração guardada antes de serem excluídos.
/// </summary>
public sealed class RemovalService
{
    private readonly QuarantineStore _store;
    private readonly SystemPaths _paths;

    public RemovalService(QuarantineStore store, SystemPaths paths)
    {
        _store = store;
        _paths = paths;
    }

    public RemovalReport Remove(InstalledApp app, IReadOnlyList<LeftoverItem> items, IProgress<string>? progress = null)
    {
        var session = _store.CreateSession(app.DisplayName);
        Log.Info($"Removendo {items.Count} sobras de '{app.DisplayName}' (quarentena {session.Id}).");

        // Serviços primeiro (liberam os executáveis), depois tarefas, depois arquivos (mais profundos antes).
        var ordered = items
            .OrderBy(i => i.Kind switch
            {
                LeftoverKind.Service => 0,
                LeftoverKind.ScheduledTask => 1,
                _ => 2,
            })
            .ThenByDescending(i => i.Target.Length)
            .ToList();

        var errors = new List<string>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            progress?.Report($"({index + 1}/{ordered.Count}) {item.Target}");

            var entry = new QuarantineEntry
            {
                Kind = item.Kind,
                Target = item.Target,
                Details = item.Details,
                RemovedAt = DateTime.Now,
            };

            try
            {
                switch (item.Kind)
                {
                    case LeftoverKind.Service:
                        RemoveService(item, entry);
                        break;

                    case LeftoverKind.ScheduledTask:
                        RemoveTask(item, entry, session);
                        break;

                    default:
                        var relative = _store.AllocateItemPath(session, item.Target);
                        entry.StoredRelativePath = relative;
                        FileOps.MoveToQuarantine(item.Target, Path.Combine(session.DirectoryPath, relative), entry);
                        break;
                }
            }
            catch (Exception ex)
            {
                entry.Status = EntryStatus.Failed;
                entry.Error = ex.Message;
                errors.Add($"{item.Target}: {ex.Message}");
                Log.Error($"Falha removendo {item.Kind} {item.Target}", ex);
            }

            Log.Info($"{entry.Status}: {item.Kind} {item.Target}");
            session.Entries.Add(entry);
            _store.Save(session);
        }

        return new RemovalReport(
            session,
            session.Entries.Count(e => e.Status is EntryStatus.Quarantined or EntryStatus.Removed),
            session.Entries.Count(e => e.Status == EntryStatus.PendingReboot),
            session.Entries.Count(e => e.Status == EntryStatus.Skipped),
            session.Entries.Count(e => e.Status == EntryStatus.Failed),
            errors);
    }

    private static void RemoveService(LeftoverItem item, QuarantineEntry entry)
    {
        var backup = ServiceOps.ReadBackup(item.Target);
        if (backup is null)
        {
            entry.Status = EntryStatus.Skipped;
            entry.Error = "O serviço não existe mais.";
            return;
        }

        entry.Service = backup;
        var stopped = ServiceOps.StopAndDelete(item.Target);
        entry.Status = EntryStatus.Removed;
        if (!stopped)
        {
            entry.Error = "O serviço não parou a tempo; a exclusão será concluída ao reiniciar.";
        }
    }

    private void RemoveTask(LeftoverItem item, QuarantineEntry entry, QuarantineSession session)
    {
        var definition = _paths.TasksFolder + item.Target;
        if (!File.Exists(definition))
        {
            entry.Status = EntryStatus.Skipped;
            entry.Error = "A tarefa não existe mais.";
            return;
        }

        var relative = _store.AllocateItemPath(session, definition, ".xml");
        var destination = Path.Combine(session.DirectoryPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(definition, destination, overwrite: true);
        entry.StoredRelativePath = relative;

        TaskOps.Delete(item.Target);
        entry.Status = EntryStatus.Removed;
    }
}
