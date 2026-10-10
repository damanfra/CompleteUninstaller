using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

/// <summary>
/// Quarentena em ~/.local/share/CompleteUninstaller/Quarantine. Cada limpeza vira uma pasta com um
/// manifest.json e os itens da pasta pessoal em "items/NNNN/nome-original". Os itens do sistema ficam na
/// quarentena de /var/lib/CompleteUninstaller (só o auxiliar com privilégios a acessa); o manifesto daqui os lista.
/// </summary>
public sealed class QuarantineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly LinuxEnvironment _environment;
    private readonly HelperClient _helper;

    public QuarantineStore(LinuxEnvironment environment, HelperClient helper, string? root = null)
    {
        _environment = environment;
        _helper = helper;
        Root = root ?? AppPaths.QuarantineRoot;
    }

    public string Root { get; }

    public QuarantineSession CreateSession(string appName)
    {
        var baseId = $"{DateTime.Now:yyyyMMdd-HHmmss}_{SanitizeForId(appName)}";
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

    /// <summary>Restaura tudo o que for restaurável: arquivos primeiro, depois os serviços.</summary>
    public RestoreReport Restore(QuarantineSession session)
    {
        var restored = 0;
        var failed = 0;
        var errors = new List<string>();

        // Ordem inversa da remoção: pastas-pai voltam antes dos itens que estavam dentro delas.
        var entries = Enumerable.Reverse(session.Entries).Where(e => e.CanRestore).ToList();
        var ordered = entries.Where(e => e.Kind != LeftoverKind.Service).Concat(entries.Where(e => e.Kind == LeftoverKind.Service)).ToList();

        var elevated = new List<(QuarantineEntry Entry, string Id)>();
        foreach (var entry in ordered)
        {
            if (entry.StoredRelativePath is null)
            {
                failed++;
                errors.Add($"{entry.Target}: cópia em quarentena ausente.");
                continue;
            }

            if (entry.Elevated)
            {
                elevated.Add((entry, ItemIdOf(entry)));
                continue;
            }

            try
            {
                FileMover.Move(Path.Combine(session.DirectoryPath, entry.StoredRelativePath), entry.Target);
                if (entry.Kind == LeftoverKind.Service)
                {
                    CommandRunner.Run("systemctl", ["--user", "daemon-reload"], timeoutMs: 30_000);
                }

                MarkRestored(entry);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                entry.Error = ex.Message;
                errors.Add($"{entry.Target}: {ex.Message}");
                Log.Error($"Falha ao restaurar {entry.Target}", ex);
            }
        }

        if (elevated.Count > 0)
        {
            var response = _helper.Send(new HelperRequest
            {
                Op = "restore",
                SessionId = session.Id,
                Items = elevated.Select(e => new HelperItem
                {
                    Id = e.Id,
                    Kind = e.Entry.Kind.ToString(),
                    Path = e.Entry.Target,
                    Stored = e.Entry.StoredRelativePath,
                }).ToList(),
            });

            foreach (var (entry, id) in elevated)
            {
                var result = response.Results.FirstOrDefault(r => r.Id == id);
                if (result is { Ok: true })
                {
                    MarkRestored(entry);
                    restored++;
                }
                else
                {
                    failed++;
                    entry.Error = result?.Error ?? response.Error ?? "Falha desconhecida no auxiliar.";
                    errors.Add($"{entry.Target}: {entry.Error}");
                }
            }
        }

        Save(session);
        return new RestoreReport(restored, failed, errors);
    }

    /// <summary>Apaga a sessão definitivamente (inclusive a parte guardada no sistema). Retorna os erros, se houver.</summary>
    public IReadOnlyList<string> Purge(QuarantineSession session)
    {
        Log.Info($"Excluindo definitivamente a quarentena {session.Id}.");
        var errors = new List<string>();

        if (session.Entries.Any(e => e.Elevated && e.CanRestore))
        {
            var response = _helper.Send(new HelperRequest { Op = "purge", SessionId = session.Id });
            if (!response.Ok)
            {
                // Sem apagar a parte do sistema, o manifesto continua para a pessoa poder tentar de novo.
                errors.Add(response.Error ?? "O auxiliar não conseguiu apagar a quarentena do sistema.");
                return errors;
            }
        }

        try
        {
            Directory.Delete(session.DirectoryPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{session.DirectoryPath}: {ex.Message}");
        }

        return errors;
    }

    /// <summary>"0001" de "items/0001/nome".</summary>
    internal static string ItemIdOf(QuarantineEntry entry) =>
        entry.StoredRelativePath!.Split('/')[1];

    private static void MarkRestored(QuarantineEntry entry)
    {
        entry.Status = EntryStatus.Restored;
        entry.Error = null;
        Log.Info($"Restaurado: {entry.Target}");
    }

    /// <summary>Id da sessão: só letras, números, "-", "_" e "." (o auxiliar valida o mesmo formato).</summary>
    private static string SanitizeForId(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            sb.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' ? c : '_');
        }

        var text = sb.ToString().Trim('_');
        if (text.Length > 60)
        {
            text = text[..60];
        }

        return text.Length == 0 ? "item" : text;
    }
}
