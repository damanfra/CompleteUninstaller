using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Infrastructure.Logging;

namespace CompleteUninstaller.Infrastructure.Removal;

/// <summary>
/// Quarentena em %ProgramData%\CompleteUninstaller\Quarantine. Cada limpeza vira uma pasta com
/// um manifest.json e os itens guardados em "items\NNNN\nome-original".
/// </summary>
public sealed class QuarantineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public QuarantineStore(string? root = null) => Root = root ?? AppPaths.QuarantineRoot;

    public string Root { get; }

    public QuarantineSession CreateSession(string appName)
    {
        var baseId = $"{DateTime.Now:yyyyMMdd-HHmmss}_{Sanitize(appName)}";
        var directory = Path.Combine(Root, baseId);
        for (var n = 2; Directory.Exists(directory); n++)
        {
            directory = Path.Combine(Root, $"{baseId}_{n}");
        }

        Directory.CreateDirectory(Path.Combine(directory, "items"));
        var session = new QuarantineSession
        {
            Id = Path.GetFileName(directory),
            AppName = appName,
            CreatedAt = DateTime.Now,
            DirectoryPath = directory,
        };
        Save(session);
        return session;
    }

    /// <summary>Reserva um caminho exclusivo dentro da sessão para guardar um item.</summary>
    public string AllocateItemPath(QuarantineSession session, string originalPath, string suffix = "")
    {
        var name = Path.GetFileName(originalPath.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "item";
        }

        var relative = Path.Combine("items", (session.Entries.Count + 1).ToString("D4"), Sanitize(name) + suffix);
        for (var n = 2; Directory.Exists(Path.Combine(session.DirectoryPath, Path.GetDirectoryName(relative)!)); n++)
        {
            relative = Path.Combine("items", $"{session.Entries.Count + 1:D4}_{n}", Sanitize(name) + suffix);
        }

        return relative;
    }

    public void Save(QuarantineSession session)
    {
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(Path.Combine(session.DirectoryPath, "manifest.json"), json, Encoding.UTF8);
    }

    public IReadOnlyList<QuarantineSession> ListSessions()
    {
        if (!Directory.Exists(Root))
        {
            return Array.Empty<QuarantineSession>();
        }

        var sessions = new List<QuarantineSession>();
        foreach (var directory in Directory.GetDirectories(Root))
        {
            var manifest = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifest))
            {
                continue;
            }

            try
            {
                var session = JsonSerializer.Deserialize<QuarantineSession>(File.ReadAllText(manifest), JsonOptions);
                if (session is null)
                {
                    continue;
                }

                session.DirectoryPath = directory;
                sessions.Add(session);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Log.Warn($"Manifesto de quarentena ilegível: {manifest} ({ex.Message})");
            }
        }

        return sessions.OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>Restaura tudo o que for restaurável: arquivos primeiro, depois tarefas e serviços.</summary>
    public RestoreReport Restore(QuarantineSession session)
    {
        var bootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var restored = 0;
        var failed = 0;
        var errors = new List<string>();

        // Ordem inversa da remoção: pastas-pai voltam antes dos itens que estavam dentro delas.
        var entries = Enumerable.Reverse(session.Entries).Where(e => e.CanRestore).ToList();
        var ordered = entries.Where(e => e.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut)
            .Concat(entries.Where(e => e.Kind == LeftoverKind.ScheduledTask))
            .Concat(entries.Where(e => e.Kind == LeftoverKind.Service));

        foreach (var entry in ordered)
        {
            if (entry.Status == EntryStatus.PendingReboot && session.CreatedAt > bootTime)
            {
                failed++;
                errors.Add($"{entry.Target}: há remoção pendente; reinicie o computador antes de restaurar este item.");
                continue;
            }

            try
            {
                switch (entry.Kind)
                {
                    case LeftoverKind.Service:
                        ServiceOps.Recreate(entry.Service ?? throw new InvalidOperationException("Backup do serviço ausente."));
                        break;

                    case LeftoverKind.ScheduledTask:
                        TaskOps.Recreate(entry.Target, Path.Combine(session.DirectoryPath, RequireStored(entry)));
                        break;

                    default:
                        FileOps.Restore(Path.Combine(session.DirectoryPath, RequireStored(entry)), entry.Target);
                        break;
                }

                entry.Status = EntryStatus.Restored;
                entry.Error = null;
                restored++;
                Log.Info($"Restaurado: {entry.Target}");
            }
            catch (Exception ex)
            {
                failed++;
                entry.Error = ex.Message;
                errors.Add($"{entry.Target}: {ex.Message}");
                Log.Error($"Falha ao restaurar {entry.Target}", ex);
            }
        }

        Save(session);
        return new RestoreReport(restored, failed, errors);
    }

    /// <summary>Apaga a sessão definitivamente. Retorna true se algo ficou para a reinicialização.</summary>
    public bool Purge(QuarantineSession session)
    {
        Log.Info($"Excluindo definitivamente a quarentena {session.Id}.");
        return FileOps.DeleteTree(session.DirectoryPath);
    }

    private static string RequireStored(QuarantineEntry entry) =>
        entry.StoredRelativePath ?? throw new InvalidOperationException("Cópia em quarentena ausente.");

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (sanitized.Length > 60)
        {
            sanitized = sanitized[..60];
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }
}
