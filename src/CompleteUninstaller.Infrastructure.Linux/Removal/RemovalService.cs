using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

/// <summary>
/// Remove as sobras selecionadas pelo usuário. Nada é apagado direto: tudo vai para a quarentena.
/// Itens da pasta pessoal são movidos pelo próprio aplicativo; itens do sistema, por uma única chamada ao
/// auxiliar com privilégios (uma senha por limpeza). Cada item é conferido de novo pela proteção de caminhos.
/// </summary>
public sealed class RemovalService
{
    private readonly LinuxEnvironment _environment;
    private readonly QuarantineStore _store;
    private readonly HelperClient _helper;

    public RemovalService(LinuxEnvironment environment, QuarantineStore store, HelperClient helper)
    {
        _environment = environment;
        _store = store;
        _helper = helper;
    }

    public RemovalReport Remove(InstalledApp app, IReadOnlyList<LeftoverItem> items, PathGuard guard, IProgress<string>? progress = null)
    {
        var session = _store.CreateSession(app.DisplayName);
        Log.Info($"Removendo {items.Count} sobras de '{app.DisplayName}' (quarentena {session.Id}).");

        // Serviços primeiro (liberam os executáveis), depois arquivos (os mais profundos antes).
        var ordered = items
            .OrderBy(i => i.Kind == LeftoverKind.Service ? 0 : 1)
            .ThenByDescending(i => i.Target.Length)
            .ToList();

        var errors = new List<string>();
        var elevated = new List<(QuarantineEntry Entry, string Id)>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var id = (index + 1).ToString("D4");
            progress?.Report($"({index + 1}/{ordered.Count}) {item.Target}");

            var entry = new QuarantineEntry
            {
                Kind = item.Kind,
                Target = item.Target,
                Details = item.Details,
                RemovedAt = DateTime.Now,
                Elevated = RequiresElevation(item.Target),
            };
            session.Entries.Add(entry);

            if (!guard.CanRemove(item.Target, out var why))
            {
                entry.Status = EntryStatus.Skipped;
                entry.Error = $"Protegido: {why}";
                Log.Warn($"Recusado na remoção: {item.Target} — {why}");
                continue;
            }

            if (entry.Elevated)
            {
                entry.Status = EntryStatus.Skipped; // atualizado depois da chamada ao auxiliar
                entry.Error = "Aguardando o auxiliar com privilégios.";
                elevated.Add((entry, id));
                continue;
            }

            try
            {
                RemoveDirect(item, entry, session, id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entry.Status = EntryStatus.Failed;
                entry.Error = ex.Message;
                errors.Add($"{item.Target}: {ex.Message}");
                Log.Error($"Falha removendo {item.Kind} {item.Target}", ex);
            }

            Log.Info($"{entry.Status}: {item.Kind} {item.Target}");
            _store.Save(session);
        }

        if (elevated.Count > 0)
        {
            progress?.Report($"Pedindo autorização para mover {elevated.Count} item(ns) do sistema...");
            var response = _helper.Send(new HelperRequest
            {
                Op = "move",
                SessionId = session.Id,
                Items = elevated.Select(e => new HelperItem { Id = e.Id, Kind = e.Entry.Kind.ToString(), Path = e.Entry.Target }).ToList(),
            });

            foreach (var (entry, id) in elevated)
            {
                var result = response.Results.FirstOrDefault(r => r.Id == id);
                if (result is { Ok: true })
                {
                    entry.Status = result.Status == "Removed" ? EntryStatus.Removed : EntryStatus.Quarantined;
                    entry.StoredRelativePath = result.Stored;
                    entry.Error = null;
                }
                else
                {
                    entry.Status = result?.Status == "Skipped" ? EntryStatus.Skipped : EntryStatus.Failed;
                    entry.Error = result?.Error ?? response.Error ?? "Falha desconhecida no auxiliar.";
                    if (entry.Status == EntryStatus.Failed)
                    {
                        errors.Add($"{entry.Target}: {entry.Error}");
                    }
                }

                Log.Info($"{entry.Status}: {entry.Kind} {entry.Target} (sistema)");
            }
        }

        _store.Save(session);
        return new RemovalReport(
            session,
            session.Entries.Count(e => e.Status is EntryStatus.Quarantined or EntryStatus.Removed),
            session.Entries.Count(e => e.Status == EntryStatus.Skipped),
            session.Entries.Count(e => e.Status == EntryStatus.Failed),
            errors);
    }

    /// <summary>Fora da pasta pessoal precisa de root (a não ser que o aplicativo já esteja como root).</summary>
    private bool RequiresElevation(string path) =>
        !_environment.IsRoot && !UnixPath.IsSameOrUnder(path, _environment.Home);

    private static void RemoveDirect(LeftoverItem item, QuarantineEntry entry, QuarantineSession session, string id)
    {
        if (!FileSystemHelper.Exists(item.Target))
        {
            entry.Status = EntryStatus.Skipped;
            entry.Error = "O item não existe mais.";
            return;
        }

        if (FileSystemHelper.IsLink(item.Target))
        {
            entry.Status = EntryStatus.Skipped;
            entry.Error = "Links simbólicos não são movidos.";
            return;
        }

        if (item.Kind == LeftoverKind.Service)
        {
            // Desativa antes de mover; se a unidade não estiver ativa, o erro do systemctl não importa.
            CommandRunner.Run("systemctl", ["--user", "disable", "--now", UnixPath.GetLeaf(item.Target)], timeoutMs: 60_000);
        }

        var relative = $"items/{id}/{UnixPath.GetLeaf(item.Target)}";
        FileMover.Move(item.Target, Path.Combine(session.DirectoryPath, relative));
        if (item.Kind == LeftoverKind.Service)
        {
            CommandRunner.Run("systemctl", ["--user", "daemon-reload"], timeoutMs: 30_000);
        }

        entry.StoredRelativePath = relative;
        entry.Status = item.Kind == LeftoverKind.Service ? EntryStatus.Removed : EntryStatus.Quarantined;
    }
}
