using System.Globalization;
using System.Text.Json;

namespace CompleteUninstaller.Core.Updates;

public sealed record ReleaseAsset(string Name, string Url, long Size);

public sealed record ReleaseInfo(
    string Tag,
    bool IsDraft,
    bool IsPrerelease,
    string Notes,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Uma atualização disponível, já com o arquivo certo para esta plataforma e o arquivo de checksums.</summary>
public sealed record UpdateOffer(
    AppVersion Version,
    string Tag,
    string Notes,
    ReleaseAsset Package,
    ReleaseAsset Checksums);

/// <param name="Offer">Há atualização para instalar.</param>
/// <param name="Message">Quando não há oferta, o motivo em português (em dia, sem arquivo para esta plataforma...).</param>
public sealed record UpdateDecision(UpdateOffer? Offer, string Message);

/// <summary>Regras puras do auto-update: ler a resposta do GitHub e decidir se vale oferecer a atualização.</summary>
public static class UpdatePlanner
{
    public const string ChecksumsFileName = "SHA256SUMS.txt";

    /// <summary>Lê o JSON de <c>GET /repos/{dono}/{repo}/releases/latest</c>.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tag)
                || tag.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    var url = Text(asset, "browser_download_url");
                    if (name.Length > 0 && url.Length > 0)
                    {
                        assets.Add(new ReleaseAsset(name, url, asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0));
                    }
                }
            }

            return new ReleaseInfo(
                tag.GetString() ?? string.Empty,
                Flag(root, "draft"),
                Flag(root, "prerelease"),
                Text(root, "body"),
                assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <param name="packageName">Nome do arquivo desta plataforma (ex.: CompleteUninstaller-win-x64.zip), ou null se não há suporte.</param>
    /// <param name="repository">"dono/repositório": os links precisam apontar para as releases dele, em HTTPS.</param>
    public static UpdateDecision Decide(ReleaseInfo? release, AppVersion current, string? packageName, string repository)
    {
        if (release is null)
        {
            return new UpdateDecision(null, "A resposta do GitHub não pôde ser lida.");
        }

        if (release.IsDraft || release.IsPrerelease)
        {
            return new UpdateDecision(null, "A última versão publicada ainda é um rascunho ou pré-lançamento.");
        }

        if (!AppVersion.TryParse(release.Tag, out var latest))
        {
            return new UpdateDecision(null, $"O número da versão publicada ('{release.Tag}') não é reconhecido.");
        }

        if (latest <= current)
        {
            return new UpdateDecision(null, $"Você já está na versão mais recente ({current}).");
        }

        if (packageName is null)
        {
            return new UpdateDecision(null, "Não há atualização automática para esta plataforma.");
        }

        var package = release.Assets.FirstOrDefault(a => a.Name == packageName);
        var checksums = release.Assets.FirstOrDefault(a => a.Name == ChecksumsFileName);
        if (package is null)
        {
            return new UpdateDecision(null, $"A versão {latest} não tem o arquivo {packageName}.");
        }

        if (checksums is null)
        {
            return new UpdateDecision(null, $"A versão {latest} não publicou o {ChecksumsFileName}; a atualização automática foi recusada por segurança.");
        }

        if (!IsTrustedUrl(package.Url, repository) || !IsTrustedUrl(checksums.Url, repository))
        {
            return new UpdateDecision(null, "Os links da release não apontam para o repositório do projeto; atualização recusada.");
        }

        return new UpdateDecision(new UpdateOffer(latest, release.Tag, release.Notes, package, checksums), $"Versão {latest} disponível.");
    }

    /// <summary>Só aceita HTTPS em github.com/{repo}/releases/download/...</summary>
    public static bool IsTrustedUrl(string url, string repository)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase)
            && !uri.AbsolutePath.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>Acha o hash de um arquivo no formato de <c>sha256sum</c>: "hash  nome" ou "hash *nome".</summary>
    public static string? FindChecksum(string checksumsText, string fileName)
    {
        foreach (var raw in checksumsText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66)
            {
                continue;
            }

            var hash = line[..64];
            var name = line[64..].TrimStart(' ', '*');
            if (name == fileName && hash.All(Uri.IsHexDigit))
            {
                return hash.ToLower(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
