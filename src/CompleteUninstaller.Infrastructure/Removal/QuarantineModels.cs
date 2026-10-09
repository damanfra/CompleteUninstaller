using System.Text.Json.Serialization;
using CompleteUninstaller.Core.Leftovers;

namespace CompleteUninstaller.Infrastructure.Removal;

public enum EntryStatus
{
    /// <summary>Movido para a quarentena; pode ser restaurado.</summary>
    Quarantined,

    /// <summary>Copiado para a quarentena; o original estava em uso e será apagado na reinicialização.</summary>
    PendingReboot,

    /// <summary>Serviço/tarefa removido com backup da configuração; pode ser recriado.</summary>
    Removed,

    Skipped,
    Failed,
    Restored,
}

/// <summary>Configuração de um serviço removido, suficiente para recriá-lo.</summary>
public sealed class ServiceBackup
{
    public string Name { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? ObjectName { get; set; }

    public int Start { get; set; }

    public int Type { get; set; }

    public bool DelayedAutoStart { get; set; }
}

public sealed class QuarantineEntry
{
    public LeftoverKind Kind { get; set; }

    public string Target { get; set; } = string.Empty;

    public string? Details { get; set; }

    /// <summary>Caminho relativo à pasta da sessão (arquivos, pastas, atalhos e XML de tarefas).</summary>
    public string? StoredRelativePath { get; set; }

    public EntryStatus Status { get; set; }

    public string? Error { get; set; }

    public bool PartialCopy { get; set; }

    public DateTime RemovedAt { get; set; }

    public ServiceBackup? Service { get; set; }

    [JsonIgnore]
    public string KindText => Kind switch
    {
        LeftoverKind.Folder => "Pasta",
        LeftoverKind.File => "Arquivo",
        LeftoverKind.Shortcut => "Atalho",
        LeftoverKind.Service => "Serviço",
        LeftoverKind.ScheduledTask => "Tarefa agendada",
        _ => Kind.ToString(),
    };

    [JsonIgnore]
    public string StatusText => Status switch
    {
        EntryStatus.Quarantined => PartialCopy ? "Em quarentena (cópia parcial)" : "Em quarentena",
        EntryStatus.PendingReboot => PartialCopy
            ? "Em quarentena (cópia parcial); remoção na reinicialização"
            : "Em quarentena; remoção na reinicialização",
        EntryStatus.Removed => "Removido (com backup)",
        EntryStatus.Skipped => "Ignorado",
        EntryStatus.Failed => "Falhou",
        EntryStatus.Restored => "Restaurado",
        _ => Status.ToString(),
    };

    [JsonIgnore]
    public bool CanRestore => Status is EntryStatus.Quarantined or EntryStatus.PendingReboot or EntryStatus.Removed;
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
    int PendingReboot,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record RestoreReport(int Restored, int Failed, IReadOnlyList<string> Errors);
