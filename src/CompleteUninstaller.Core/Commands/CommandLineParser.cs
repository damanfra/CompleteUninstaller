using System.Text.RegularExpressions;
using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Core.Commands;

public sealed record ParsedCommand(string FileName, string Arguments);

/// <summary>Interpreta linhas de comando no estilo das chaves UninstallString/DisplayIcon/ImagePath.</summary>
public static class CommandLineParser
{
    private static readonly Regex MsiProductCode = new(
        @"msiexec(?:\.exe)?""?\s+.*?[/-][ix]\s*""?(\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Separa executável e argumentos. Lida com caminhos sem aspas contendo espaços
    /// ("C:\Program Files\X\uninst.exe /S") testando prefixos com <paramref name="fileExists"/>.
    /// </summary>
    public static ParsedCommand? Parse(string? commandLine, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var s = commandLine.Trim();
        if (s[0] == '"')
        {
            var end = s.IndexOf('"', 1);
            return end < 0
                ? new ParsedCommand(s.Trim('"'), string.Empty)
                : new ParsedCommand(s[1..end], s[(end + 1)..].Trim());
        }

        var parts = s.Split(' ');
        for (var i = 1; i <= parts.Length; i++)
        {
            var candidate = string.Join(' ', parts, 0, i);
            var exists = fileExists(candidate)
                || (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && fileExists(candidate + ".exe"));
            if (exists)
            {
                return new ParsedCommand(candidate, string.Join(' ', parts, i, parts.Length - i).Trim());
            }
        }

        for (var i = 1; i <= parts.Length; i++)
        {
            var candidate = string.Join(' ', parts, 0, i);
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ParsedCommand(candidate, string.Join(' ', parts, i, parts.Length - i).Trim());
            }
        }

        return new ParsedCommand(parts[0], string.Join(' ', parts, 1, parts.Length - 1).Trim());
    }

    /// <summary>Extrai o ProductCode de "MsiExec.exe /I{...}" ou "msiexec /x {...}".</summary>
    public static string? ExtractMsiProductCode(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var match = MsiProductCode.Match(commandLine);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>"C:\App\app.exe,0" ou "\"C:\App\app.exe\",0" -> "C:\App\app.exe".</summary>
    public static string? ExtractIconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var s = displayIcon.Trim();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : s.Trim('"');
        }

        var comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out _))
        {
            s = s[..comma];
        }

        return s.Trim();
    }

    public static bool IsMsiExec(string fileName)
    {
        var leaf = WinPath.GetLeaf(fileName);
        return leaf.Equals("msiexec", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGuid(string? value) =>
        value is { Length: 38 } && value[0] == '{' && value[^1] == '}' && Guid.TryParse(value, out _);
}
