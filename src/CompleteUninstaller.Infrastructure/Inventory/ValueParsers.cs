using System.Globalization;
using Microsoft.Win32;

namespace CompleteUninstaller.Infrastructure.Inventory;

internal static class ValueParsers
{
    private static readonly string[] DateFormats = ["yyyyMMdd", "yyyy-MM-dd", "M/d/yyyy", "d/M/yyyy", "dd/MM/yyyy"];

    public static string? GetString(RegistryKey key, string name)
    {
        var value = key.GetValue(name)?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static int? GetInt(RegistryKey key, string name) => key.GetValue(name) switch
    {
        int i => i,
        long l => (int)l,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => null,
    };

    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Expande variáveis de ambiente e remove aspas; devolve null para valores vazios.</summary>
    public static string? CleanPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"').Trim());
        return string.IsNullOrWhiteSpace(expanded) ? null : expanded;
    }
}
