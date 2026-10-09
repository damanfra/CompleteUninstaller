using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CompleteUninstaller.Core.Text;

/// <summary>
/// Normaliza nomes de programas, fabricantes e pastas para comparação:
/// minúsculas, sem acentos, sem números de versão, sem arquitetura (x64, 64-bit...),
/// sem idioma (pt-BR...) e sem pontuação.
/// Ex.: "Mozilla Firefox (x64 pt-BR)" -> "mozilla firefox".
/// </summary>
public static class NameNormalizer
{
    private static readonly HashSet<string> ArchitectureTokens = new(StringComparer.Ordinal)
    {
        "x64", "x86", "amd64", "arm64", "win64", "win32", "64bit", "32bit", "64bits", "32bits",
    };

    private static readonly HashSet<string> CorporateSuffixes = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "llc", "ltd", "limited", "gmbh", "corp", "corporation", "co", "company",
        "sa", "ltda", "ag", "bv", "srl", "spa", "plc", "pty", "kg", "oy", "ab", "as", "sas", "sarl",
        "eireli", "me", "the", "s", "a",
    };

    private static readonly Regex Parenthetical = new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);

    private static readonly Regex BitnessWords = new(
        @"\b(?:32|64)[\s\-]?bits?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Locale = new(
        @"\b(?:en|pt|es|fr|de|it|ja|zh|ko|ru|nl|pl|sv|tr|cs|da|fi|nb|no|hu|el|he|ar|uk|ro|sk|th|vi|id)-[a-z]{2,4}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Version = new(
        @"\bv?\d+(?:[.,_]\d+)+[a-z]?\b|\bv\d+\b|\bbuild\s*\d+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NonWord = new(@"[^\p{L}\p{N}+#]+", RegexOptions.Compiled);

    public static string Normalize(string? value, bool stripParentheticals = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = RemoveDiacritics(value).ToLowerInvariant();
        if (stripParentheticals)
        {
            s = Parenthetical.Replace(s, " ");
        }

        s = BitnessWords.Replace(s, " ");
        s = Locale.Replace(s, " ");
        s = Version.Replace(s, " ");
        s = NonWord.Replace(s, " ");

        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !ArchitectureTokens.Contains(t));
        return string.Join(' ', tokens);
    }

    /// <summary>Como <see cref="Normalize"/>, removendo também sufixos societários (Inc., LLC, Ltda...).</summary>
    public static string NormalizePublisher(string? publisher)
    {
        var tokens = Tokens(Normalize(publisher)).Where(t => !CorporateSuffixes.Contains(t));
        return string.Join(' ', tokens);
    }

    /// <summary>Remove os espaços: "visual studio code" -> "visualstudiocode".</summary>
    public static string Compact(string normalized) =>
        normalized.Replace(" ", string.Empty, StringComparison.Ordinal);

    public static string[] Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
