using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using CompleteUninstaller.Core.Updates;

namespace CompleteUninstaller.Updater;

public enum PackageKind
{
    Zip,
    TarGz,
}

/// <summary>O arquivo da release desta plataforma e o nome do executável que fica dentro dele.</summary>
public sealed record PlatformPackage(string AssetName, string EntryName, PackageKind Kind)
{
    public static PlatformPackage? Current =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64
            ? null
            : OperatingSystem.IsWindows()
                ? new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip)
                : OperatingSystem.IsLinux()
                    ? new PlatformPackage("CompleteUninstaller-linux-x64.tar.gz", "complete-uninstaller", PackageKind.TarGz)
                    : null;
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message)
        : base(message)
    {
    }
}

/// <summary>Resultado de verificar atualizações: a decisão, ou o erro de rede (sem internet, repositório privado, limite da API).</summary>
public sealed record UpdateCheck(UpdateDecision? Decision, string? Error);

/// <summary>
/// Verifica e instala atualizações a partir das Releases do GitHub. Só instala depois de conferir o SHA-256 do
/// arquivo com o SHA256SUMS.txt da mesma release; os links precisam ser de github.com/{repositório}/releases.
/// </summary>
public sealed class UpdateService
{
    public const string DefaultRepository = "damanfra/CompleteUninstaller";

    /// <summary>O pacote tem uns 70 a 150 MB; acima disso é suspeito.</summary>
    private const long MaxPackageBytes = 500L * 1024 * 1024;

    private const long MaxChecksumBytes = 1024 * 1024;

    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly PlatformPackage? _platform;

    public UpdateService(HttpClient? http = null, string? repository = null, PlatformPackage? platform = null, bool useCurrentPlatform = true)
    {
        _http = http ?? CreateClient();
        _repository = repository ?? DefaultRepository;
        _platform = useCurrentPlatform && platform is null ? PlatformPackage.Current : platform;
    }

    /// <summary>A versão deste executável (a que a Action carimba a partir da tag).</summary>
    public static AppVersion CurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString();
        return AppVersion.TryParse(text, out var version) ? version : AppVersion.Zero;
    }

    public async Task<UpdateCheck> CheckAsync(AppVersion current, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repository}/releases/latest");
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheck(null, "Nenhuma release encontrada (ou o repositório ainda é privado).");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheck(null, $"O GitHub respondeu {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var decision = UpdatePlanner.Decide(UpdatePlanner.ParseRelease(json), current, _platform?.AssetName, _repository);
            return new UpdateCheck(decision, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new UpdateCheck(null, $"Não foi possível consultar o GitHub: {ex.Message}");
        }
    }

    /// <summary>Executável em condições de se auto-atualizar? (Não quando roda via "dotnet app.dll" ou em pasta sem escrita.)</summary>
    public static bool CanSelfUpdate(string? exePath, out string reason)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            reason = "Não foi possível localizar o executável.";
            return false;
        }

        if (Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            reason = "O aplicativo está rodando pelo comando dotnet; a atualização automática só vale para o executável publicado.";
            return false;
        }

        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(exePath)!, $".cu-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"Sem permissão para gravar em {Path.GetDirectoryName(exePath)}. Baixe a nova versão manualmente.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Baixa, confere o SHA-256, extrai e troca o executável. Depois, chame <see cref="Restart"/>.</summary>
    public async Task ApplyAsync(UpdateOffer offer, string exePath, IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        if (_platform is null)
        {
            throw new UpdateException("Não há atualização automática para esta plataforma.");
        }

        if (!CanSelfUpdate(exePath, out var reason))
        {
            throw new UpdateException(reason);
        }

        if (!UpdatePlanner.IsTrustedUrl(offer.Package.Url, _repository) || !UpdatePlanner.IsTrustedUrl(offer.Checksums.Url, _repository))
        {
            throw new UpdateException("Os links da release não são do repositório do projeto.");
        }

        var workDir = Path.Combine(Path.GetDirectoryName(exePath)!, $".cu-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            progress?.Report("Baixando a lista de verificação (SHA-256)...");
            var sums = await DownloadTextAsync(offer.Checksums.Url, cancellationToken);
            var expected = UpdatePlanner.FindChecksum(sums, offer.Package.Name)
                ?? throw new UpdateException($"O {UpdatePlanner.ChecksumsFileName} não lista o arquivo {offer.Package.Name}.");

            var packagePath = Path.Combine(workDir, "package");
            var actual = await DownloadFileAsync(offer.Package.Url, packagePath, progress, cancellationToken);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("O arquivo baixado não confere com o SHA-256 publicado; nada foi instalado.");
            }

            progress?.Report("Verificação concluída. Extraindo...");
            var newExe = Path.Combine(workDir, "new-executable");
            Extract(packagePath, _platform, newExe);
            if (new FileInfo(newExe).Length < 1024 * 1024)
            {
                throw new UpdateException("O executável extraído é pequeno demais; nada foi instalado.");
            }

            progress?.Report("Instalando a nova versão...");
            SelfReplacer.Replace(exePath, newExe, OperatingSystem.IsWindows());
            progress?.Report($"Versão {offer.Version} instalada.");
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    /// <summary>Abre o executável novo. Depois o aplicativo atual deve se encerrar.</summary>
    public static void Restart(string exePath)
    {
        var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = OperatingSystem.IsWindows(), WorkingDirectory = Path.GetDirectoryName(exePath)! };
        Process.Start(startInfo)?.Dispose();
    }

    /// <summary>No Windows o executável antigo é renomeado para ".old" na troca; apaga na próxima abertura.</summary>
    public static void CleanupOldVersion()
    {
        try
        {
            if (Environment.ProcessPath is { } path)
            {
                var old = path + ".old";
                if (File.Exists(old))
                {
                    File.Delete(old);
                }

                foreach (var leftover in Directory.GetDirectories(Path.GetDirectoryName(path)!, ".cu-update-*"))
                {
                    TryDeleteDirectory(leftover);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Melhor esforço: o arquivo antigo pode ainda estar em uso.
        }
    }

    internal static void Extract(string packagePath, PlatformPackage platform, string destination)
    {
        if (platform.Kind == PackageKind.Zip)
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName == platform.EntryName)
                ?? throw new UpdateException($"O pacote não contém {platform.EntryName}.");
            using var input = entry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
            return;
        }

        using var file = File.OpenRead(packagePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } tarEntry)
        {
            if (tarEntry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile
                && tarEntry.Name.TrimStart('.', '/') == platform.EntryName && tarEntry.DataStream is { } data)
            {
                using var output = File.Create(destination);
                data.CopyTo(output);
                return;
            }
        }

        throw new UpdateException($"O pacote não contém {platform.EntryName}.");
    }

    private async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxChecksumBytes)
        {
            throw new UpdateException("O arquivo de verificação é grande demais.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Baixa para o disco calculando o SHA-256 no caminho. Devolve o hash em hexadecimal minúsculo.</summary>
    private async Task<string> DownloadFileAsync(string url, string destination, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        if (total > MaxPackageBytes)
        {
            throw new UpdateException("O arquivo da atualização é grande demais; recusado.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long received = 0;
        var lastReport = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            received += read;
            if (received > MaxPackageBytes)
            {
                throw new UpdateException("O arquivo da atualização passou do tamanho máximo; recusado.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (total > 0 && progress is not null)
            {
                var percent = (int)(received * 100 / total.Value);
                if (percent >= lastReport + 5)
                {
                    lastReport = percent;
                    progress.Report($"Baixando a atualização... {percent}% ({received / (1024 * 1024)} de {total.Value / (1024 * 1024)} MB)");
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CompleteUninstaller-Updater", CurrentVersion().ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Troca o executável em uso pelo novo.</summary>
public static class SelfReplacer
{
    /// <param name="windowsStyle">
    /// Windows: o executável em uso não pode ser sobrescrito, mas pode ser renomeado; ele vira ".old" e é apagado
    /// na próxima abertura. Linux: o arquivo novo substitui o antigo por renomeação atômica (o processo atual segue
    /// usando o inode antigo até terminar).
    /// </param>
    public static void Replace(string exePath, string newFile, bool windowsStyle)
    {
        if (windowsStyle)
        {
            var old = exePath + ".old";
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(exePath, old);
            try
            {
                File.Move(newFile, exePath);
            }
            catch
            {
                File.Move(old, exePath); // desfaz: o aplicativo continua inteiro
                throw;
            }

            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(newFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        File.Move(newFile, exePath, overwrite: true);
    }
}
