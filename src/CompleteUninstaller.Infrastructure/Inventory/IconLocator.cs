using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Inventory;

/// <summary>Origem de um ícone: arquivo com recurso de ícone (.exe/.dll/.ico + índice) ou imagem (.png).</summary>
public sealed record IconSource(string Path, int Index, bool IsImageFile);

/// <summary>
/// Descobre de onde tirar o ícone oficial de um programa:
/// 1) DisplayIcon do Registro; 2) logotipo do pacote da Store; 3) executável principal na pasta de instalação.
/// </summary>
public static class IconLocator
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private static readonly string[] IgnoredExecutableWords =
        ["unins", "uninst", "setup", "install", "update", "crash", "helper", "report", "elevat", "service"];

    public static IconSource? Locate(InstalledApp app)
    {
        try
        {
            if (app.Source == AppSource.Store)
            {
                return ResolveStoreLogo(app.LogoPath) is { } logo ? new IconSource(logo, 0, true) : null;
            }

            return FromDisplayIcon(app.DisplayIcon) ?? FromInstallLocation(app);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"C:\App\app.exe,0", "\"C:\App\app.exe\",-101" ou "C:\App\app.ico".</summary>
    private static IconSource? FromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var text = displayIcon.Trim();
        string path;
        var index = 0;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            path = end > 1 ? text[1..end] : text.Trim('"');
            var rest = end > 1 ? text[(end + 1)..].Trim().TrimStart(',').Trim() : string.Empty;
            _ = int.TryParse(rest, out index);
        }
        else
        {
            path = text;
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out var parsed))
            {
                path = text[..comma].Trim();
                index = parsed;
            }
        }

        path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
        if (!File.Exists(path))
        {
            return null;
        }

        var isImage = ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        return new IconSource(path, index, isImage);
    }

    /// <summary>Executável principal na pasta de instalação (com o nome do programa, ou o único candidato).</summary>
    private static IconSource? FromInstallLocation(InstalledApp app)
    {
        var location = app.InstallLocation is null ? null : Environment.ExpandEnvironmentVariables(app.InstallLocation.Trim('"'));
        if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location))
        {
            return null;
        }

        var candidates = FileSystemHelper.GetFiles(location, "*.exe")
            .Where(f => !IgnoredExecutableWords.Any(w =>
                Path.GetFileNameWithoutExtension(f).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var matcher = new AppNameMatcher(app.DisplayName, app.Publisher);
        var best = candidates.FirstOrDefault(f => matcher.Match(Path.GetFileNameWithoutExtension(f)) == NameMatch.Exact)
            ?? candidates.FirstOrDefault(f => matcher.Match(Path.GetFileNameWithoutExtension(f)) == NameMatch.Partial)
            ?? (candidates.Count == 1 ? candidates[0] : null);

        return best is null ? null : new IconSource(best, 0, false);
    }

    /// <summary>
    /// O manifesto aponta para "Assets\StoreLogo.png", mas no disco o arquivo costuma ter qualificadores:
    /// "StoreLogo.scale-100.png", "StoreLogo.targetsize-48.png"...
    /// </summary>
    private static string? ResolveStoreLogo(string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        if (File.Exists(logoPath))
        {
            return logoPath;
        }

        var directory = Path.GetDirectoryName(logoPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(logoPath);
        var extension = Path.GetExtension(logoPath);
        var variants = FileSystemHelper.GetFiles(directory, $"{name}.*{extension}");
        if (variants.Count == 0)
        {
            return null;
        }

        string[] preferred = ["scale-100", "targetsize-48", "scale-125", "targetsize-32", "scale-150", "scale-200"];
        foreach (var qualifier in preferred)
        {
            var match = variants.FirstOrDefault(v => v.Contains(qualifier, StringComparison.OrdinalIgnoreCase)
                && !v.Contains("contrast", StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return variants.FirstOrDefault(v => !v.Contains("contrast", StringComparison.OrdinalIgnoreCase)) ?? variants[0];
    }
}
