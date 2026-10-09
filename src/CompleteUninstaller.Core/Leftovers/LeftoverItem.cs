namespace CompleteUninstaller.Core.Leftovers;

public enum LeftoverKind
{
    Folder,
    File,
    Shortcut,
    Service,
    ScheduledTask,
}

public enum Confidence
{
    Medium = 1,
    High = 2,
}

public enum CleanupLevel
{
    /// <summary>Somente itens com ligação comprovada ao programa (confiança alta).</summary>
    Safe,

    /// <summary>Inclui itens identificados por semelhança de nome (confiança média), para revisão.</summary>
    Moderate,
}

/// <summary>Uma sobra encontrada após a desinstalação.</summary>
public sealed class LeftoverItem
{
    public required LeftoverKind Kind { get; init; }

    /// <summary>Caminho (pasta/arquivo/atalho), nome do serviço ou caminho da tarefa agendada.</summary>
    public required string Target { get; init; }

    public required Confidence Confidence { get; set; }

    public required string Reason { get; set; }

    /// <summary>Informação complementar (executável do serviço, comando da tarefa...).</summary>
    public string? Details { get; init; }

    public long? SizeBytes { get; set; }

    public bool IsFileSystem => Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut;
}
