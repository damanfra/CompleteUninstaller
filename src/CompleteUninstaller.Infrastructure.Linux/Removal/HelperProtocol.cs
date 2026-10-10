using System.Text.Json;
using System.Text.RegularExpressions;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

/// <summary>Pedido do aplicativo (sem privilégios) ao auxiliar que roda como root via pkexec.</summary>
public sealed class HelperRequest
{
    /// <summary>"move", "restore" ou "purge".</summary>
    public string Op { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public List<HelperItem> Items { get; set; } = [];
}

public sealed class HelperItem
{
    /// <summary>Identificador do item na sessão ("0001").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>"Folder", "File", "Shortcut" ou "Service".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Caminho original do item.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Só no "restore": caminho relativo na quarentena ("items/0001/nome").</summary>
    public string? Stored { get; set; }
}

public sealed class HelperResult
{
    public string Id { get; set; } = string.Empty;

    public bool Ok { get; set; }

    /// <summary>"Quarantined", "Removed", "Skipped" ou "Restored".</summary>
    public string Status { get; set; } = string.Empty;

    public string? Stored { get; set; }

    public string? Error { get; set; }
}

public sealed class HelperResponse
{
    public bool Ok { get; set; }

    public string? Error { get; set; }

    public List<HelperResult> Results { get; set; } = [];
}

/// <summary>Formato e validação do protocolo. O auxiliar valida tudo de novo: o pedido nunca é confiável.</summary>
public static partial class HelperProtocol
{
    public const int MaxItems = 5000;

    public const int MaxRequestBytes = 4 * 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z_.\-]{0,200}$")]
    private static partial Regex SessionIdPattern();

    [GeneratedRegex(@"^[0-9]{4,6}(_[0-9]{1,3})?$")]
    private static partial Regex ItemIdPattern();

    [GeneratedRegex(@"^items/[0-9]{4,6}(_[0-9]{1,3})?/[^/\0]{1,255}$")]
    private static partial Regex StoredPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9:_.@\\\-]*\.(service|timer)$")]
    private static partial Regex UnitNamePattern();

    public static bool IsValidSessionId(string value) =>
        SessionIdPattern().IsMatch(value) && !value.Contains("..", StringComparison.Ordinal);

    public static bool IsValidItemId(string value) => ItemIdPattern().IsMatch(value);

    public static bool IsValidStored(string value, string itemId) =>
        StoredPattern().IsMatch(value)
        && value.StartsWith($"items/{itemId}/", StringComparison.Ordinal)
        && value[(value.LastIndexOf('/') + 1)..] is not ("." or "..");

    public static bool IsValidUnitName(string value) => UnitNamePattern().IsMatch(value);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}
