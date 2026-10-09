using System.Text.Json.Serialization;
using CompleteUninstaller.Core.Leftovers;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

public enum EntryStatus
{
    /// <summary>Movido para a quarentena; pode ser restaurado.</summary>
    Quarantined,

    /// <summary>Serviço desativado e movido para a quarentena; pode ser restaurado (reative-o depois).</summary>
    Removed,

    Skipped,
    Failed,
    Restored,
}

public sealed class QuarantineEntry
{
    public LeftoverKind Kind { get; set; }

    public string Target { get; set; } = string.Empty;

    public string? Details { get; set; }

    /// <summary>Caminho relativo à pasta da sessão ("items/0001/nome").</summary>
    public string? StoredRelativePath { get; set; }

    /// <summary>Guardado na quarentena do sistema (/var/lib/CompleteUninstaller), que só o auxiliar com privilégios acessa.</summary>
    public bool Elevated { get; set; }

    public EntryStatus Status { get; set; }

    public string? Error { get; set; }

    public DateTime RemovedAt { get; set; }

    [JsonIgnore]
    public string KindText => Kind switch
    {
        LeftoverKind.Folder => "Pasta",
        LeftoverKind.File => "Arquivo",
        LeftoverKind.Shortcut => "Atalho (.desktop)",
        LeftoverKind.Service => "Serviço (systemd)",
        _ => Kind.ToString(),
    };

    [JsonIgnore]
    public string StatusText => Status switch
    {
        EntryStatus.Quarantined => Elevated ? "Em quarentena (sistema)" : "Em quarentena",
        EntryStatus.Removed => "Removido (guardado na quarentena)",
        EntryStatus.Skipped => "Ignorado",
        EntryStatus.Failed => "Falhou",
        EntryStatus.Restored => "Restaurado",
        _ => Status.ToString(),
    };

    [JsonIgnore]
    public bool CanRestore => Status is EntryStatus.Quarantined or EntryStatus.Removed;
}

/// <summary>Uma operação de limpeza (todas as sobras removidas de um programa de uma vez).</summary>
public sealed class QuarantineSession
{
    public string Id { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<QuarantineEntry> Entries { get; set; } = [];

    [JsonIgnore]
    public string DirectoryPath { get; set; } = string.Empty;

    [JsonIgnore]
    public int RestorableCount => Entries.Count(e => e.CanRestore);

    [JsonIgnore]
    public string Summary => $"{CreatedAt:dd/MM/yyyy HH:mm} · {Entries.Count} itens · {RestorableCount} restauráveis";
}

public sealed record RemovalReport(
    QuarantineSession Session,
    int Succeeded,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record RestoreReport(int Restored, int Failed, IReadOnlyList<string> Errors);
