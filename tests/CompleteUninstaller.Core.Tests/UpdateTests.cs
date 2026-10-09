using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CompleteUninstaller.Core.Updates;
using CompleteUninstaller.Updater;

namespace CompleteUninstaller.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3+abc123", "1.2.3", 0)]               // metadados de build não contam
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("0.1.0", "0.2.0", -1)]
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]              // pré-versão vale menos
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)]      // número compara como número
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]             // numérico vale menos que texto
    [InlineData("0.0.0-dev", "0.1.0", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.2.3.4", "1.2.3", 1)]
    public void Comparison(string a, string b, int expected)
    {
        Assert.True(AppVersion.TryParse(a, out var left));
        Assert.True(AppVersion.TryParse(b, out var right));
        Assert.Equal(expected, Math.Sign(left.CompareTo(right)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    [InlineData(null)]
    public void Invalid(string? text) => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void ToString_is_normalized()
    {
        Assert.True(AppVersion.TryParse("v1.2", out var version));
        Assert.Equal("1.2.0", version.ToString());
    }
}

public class UpdatePlannerTests
{
    private const string Repo = "damanfra/CompleteUninstaller";
    private const string Base = "https://github.com/damanfra/CompleteUninstaller/releases/download/v0.2.0/";

    private static string Json(string tag = "v0.2.0", bool prerelease = false, bool draft = false, string extraAssets = "") =>
        $$"""
        {
          "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "body": "Novidades",
          "assets": [
            {"name": "CompleteUninstaller-win-x64.zip", "size": 1000, "browser_download_url": "{{Base}}CompleteUninstaller-win-x64.zip"},
            {"name": "CompleteUninstaller-linux-x64.tar.gz", "size": 2000, "browser_download_url": "{{Base}}CompleteUninstaller-linux-x64.tar.gz"},
            {"name": "SHA256SUMS.txt", "size": 200, "browser_download_url": "{{Base}}SHA256SUMS.txt"}{{extraAssets}}
          ]
        }
        """;

    private static AppVersion V(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        return version;
    }

    [Fact]
    public void Newer_release_is_offered_with_the_right_files()
    {
        var decision = UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json()), V("0.1.0"), "CompleteUninstaller-linux-x64.tar.gz", Repo);

        var offer = Assert.IsType<UpdateOffer>(decision.Offer);
        Assert.Equal("0.2.0", offer.Version.ToString());
        Assert.Equal("CompleteUninstaller-linux-x64.tar.gz", offer.Package.Name);
        Assert.Equal("SHA256SUMS.txt", offer.Checksums.Name);
        Assert.Equal("Novidades", offer.Notes);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    [InlineData("1.0.0")]
    public void Same_or_older_release_is_not_offered(string current)
    {
        var decision = UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json()), V(current), "CompleteUninstaller-win-x64.zip", Repo);

        Assert.Null(decision.Offer);
    }

    [Fact]
    public void Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json(prerelease: true)), V("0.1.0"), "CompleteUninstaller-win-x64.zip", Repo).Offer);
        Assert.Null(UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json(draft: true)), V("0.1.0"), "CompleteUninstaller-win-x64.zip", Repo).Offer);
    }

    [Fact]
    public void Missing_checksums_refuse_the_update()
    {
        var json = Json().Replace("SHA256SUMS.txt", "outro.txt", StringComparison.Ordinal);

        var decision = UpdatePlanner.Decide(UpdatePlanner.ParseRelease(json), V("0.1.0"), "CompleteUninstaller-win-x64.zip", Repo);

        Assert.Null(decision.Offer);
        Assert.Contains("SHA256SUMS", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_without_package_is_not_offered()
    {
        Assert.Null(UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json()), V("0.1.0"), null, Repo).Offer);
        Assert.Null(UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json()), V("0.1.0"), "CompleteUninstaller-macos.zip", Repo).Offer);
    }

    [Theory]
    [InlineData("https://github.com/damanfra/CompleteUninstaller/releases/download/v1/a.zip", true)]
    [InlineData("http://github.com/damanfra/CompleteUninstaller/releases/download/v1/a.zip", false)]      // sem HTTPS
    [InlineData("https://evil.example/damanfra/CompleteUninstaller/releases/download/v1/a.zip", false)]
    [InlineData("https://github.com/outro/Repo/releases/download/v1/a.zip", false)]
    [InlineData("https://github.com.evil.example/damanfra/CompleteUninstaller/releases/download/v1/a.zip", false)]
    [InlineData("https://github.com/damanfra/CompleteUninstaller/releases/download/../../../x", false)]
    [InlineData("https://github.com/damanfra/CompleteUninstaller/archive/main.zip", false)]
    [InlineData("não é url", false)]
    public void Trusted_urls(string url, bool expected) =>
        Assert.Equal(expected, UpdatePlanner.IsTrustedUrl(url, Repo));

    [Fact]
    public void Untrusted_links_refuse_the_update()
    {
        var json = Json().Replace("https://github.com/damanfra", "https://evil.example/damanfra", StringComparison.Ordinal);

        Assert.Null(UpdatePlanner.Decide(UpdatePlanner.ParseRelease(json), V("0.1.0"), "CompleteUninstaller-win-x64.zip", Repo).Offer);
    }

    [Fact]
    public void Garbage_json_is_handled()
    {
        Assert.Null(UpdatePlanner.ParseRelease("isto não é json"));
        Assert.Null(UpdatePlanner.ParseRelease("[]"));
        Assert.Null(UpdatePlanner.ParseRelease("{\"sem_tag\": 1}"));
        Assert.Null(UpdatePlanner.Decide(null, V("0.1.0"), "x", Repo).Offer);
    }

    [Fact]
    public void Unrecognized_tag_is_not_offered() =>
        Assert.Null(UpdatePlanner.Decide(UpdatePlanner.ParseRelease(Json(tag: "nightly")), V("0.1.0"), "CompleteUninstaller-win-x64.zip", Repo).Offer);

    [Fact]
    public void Checksum_lookup_accepts_text_and_binary_marks()
    {
        var hashA = new string('a', 64);
        var hashB = new string('B', 64);
        var text = $"{hashA}  CompleteUninstaller-win-x64.zip\n{hashB} *CompleteUninstaller-linux-x64.tar.gz\nlixo\n";

        Assert.Equal(hashA, UpdatePlanner.FindChecksum(text, "CompleteUninstaller-win-x64.zip"));
        Assert.Equal(new string('b', 64), UpdatePlanner.FindChecksum(text, "CompleteUninstaller-linux-x64.tar.gz"));
        Assert.Null(UpdatePlanner.FindChecksum(text, "outro.zip"));
        Assert.Null(UpdatePlanner.FindChecksum($"{new string('z', 64)}  a.zip", "a.zip")); // não é hexadecimal
    }
}

/// <summary>Atualização de ponta a ponta com um servidor de mentira: baixar, conferir o SHA-256, extrair e trocar o arquivo.</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private const string Repo = "damanfra/CompleteUninstaller";
    private const string Base = "https://github.com/damanfra/CompleteUninstaller/releases/download/v9.0.0/";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cu-update-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _newExecutable = RandomNumberGenerator.GetBytes(1_300_000);

    public UpdateServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private byte[] ZipWith(string entryName)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var output = entry.Open();
            output.Write(_newExecutable);
        }

        return stream.ToArray();
    }

    private byte[] TarGzWith(string entryName)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = new MemoryStream(_newExecutable) };
            tar.WriteEntry(entry);
        }

        return stream.ToArray();
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static UpdateOffer Offer(PlatformPackage platform) => new(
        AppVersion.TryParse("9.0.0", out var v) ? v : AppVersion.Zero,
        "v9.0.0",
        string.Empty,
        new ReleaseAsset(platform.AssetName, Base + platform.AssetName, 0),
        new ReleaseAsset("SHA256SUMS.txt", Base + "SHA256SUMS.txt", 0));

    private (UpdateService Service, string ExePath, byte[] OldBytes) Arrange(PlatformPackage platform, byte[] package, string? checksumOverride = null)
    {
        var exePath = Path.Combine(_dir, platform.EntryName);
        var oldBytes = Encoding.UTF8.GetBytes("versão antiga");
        File.WriteAllBytes(exePath, oldBytes);

        var sums = $"{checksumOverride ?? Sha(package)}  {platform.AssetName}\n";
        var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            var url when url.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums) },
            var url when url.EndsWith(platform.AssetName, StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        return (new UpdateService(new HttpClient(handler), Repo, platform), exePath, oldBytes);
    }

    [Fact]
    public async Task Zip_update_replaces_the_executable_and_keeps_the_old_one_aside()
    {
        var platform = new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip);
        var (service, exePath, _) = Arrange(platform, ZipWith(platform.EntryName));
        var messages = new List<string>();

        await service.ApplyAsync(Offer(platform), exePath, new Progress<string>(messages.Add));

        Assert.Equal(_newExecutable, await File.ReadAllBytesAsync(exePath));
        Assert.Empty(Directory.GetDirectories(_dir, ".cu-update-*")); // pasta de trabalho apagada
        if (OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(exePath + ".old")); // o executável em uso é renomeado, não sobrescrito
        }
    }

    [Fact]
    public async Task TarGz_update_extracts_the_executable()
    {
        var platform = new PlatformPackage("CompleteUninstaller-linux-x64.tar.gz", "complete-uninstaller", PackageKind.TarGz);
        var (service, exePath, _) = Arrange(platform, TarGzWith("./" + platform.EntryName));

        await service.ApplyAsync(Offer(platform), exePath, null);

        Assert.Equal(_newExecutable, await File.ReadAllBytesAsync(exePath));
    }

    [Fact]
    public async Task Wrong_checksum_installs_nothing()
    {
        var platform = new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip);
        var (service, exePath, oldBytes) = Arrange(platform, ZipWith(platform.EntryName), checksumOverride: new string('0', 64));

        var error = await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAsync(Offer(platform), exePath, null));

        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(exePath));
        Assert.Empty(Directory.GetDirectories(_dir, ".cu-update-*"));
    }

    [Fact]
    public async Task Package_without_the_executable_installs_nothing()
    {
        var platform = new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip);
        var (service, exePath, oldBytes) = Arrange(platform, ZipWith("outro-nome.exe"));

        await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAsync(Offer(platform), exePath, null));

        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(exePath));
    }

    [Fact]
    public async Task Tiny_executable_is_refused()
    {
        var platform = new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var output = zip.CreateEntry(platform.EntryName).Open();
            output.Write("pequeno"u8);
        }

        var (service, exePath, oldBytes) = Arrange(platform, stream.ToArray());

        await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAsync(Offer(platform), exePath, null));

        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(exePath));
    }

    [Fact]
    public async Task Offer_with_untrusted_links_is_refused_before_any_download()
    {
        var platform = new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip);
        var (service, exePath, _) = Arrange(platform, ZipWith(platform.EntryName));
        var bad = Offer(platform) with { Package = new ReleaseAsset(platform.AssetName, "https://evil.example/x.zip", 0) };

        await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAsync(bad, exePath, null));
    }

    [Fact]
    public void Dotnet_host_and_missing_files_cannot_self_update()
    {
        Assert.False(UpdateService.CanSelfUpdate(Path.Combine(_dir, "nao-existe.exe"), out _));

        var host = Path.Combine(_dir, "dotnet.exe");
        File.WriteAllText(host, "x");
        Assert.False(UpdateService.CanSelfUpdate(host, out var reason));
        Assert.Contains("dotnet", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_reports_a_newer_release()
    {
        var platform = new PlatformPackage("CompleteUninstaller-win-x64.zip", "CompleteUninstaller.exe", PackageKind.Zip);
        var json = $$"""
            {"tag_name":"v9.0.0","draft":false,"prerelease":false,"body":"x","assets":[
             {"name":"CompleteUninstaller-win-x64.zip","size":1,"browser_download_url":"{{Base}}CompleteUninstaller-win-x64.zip"},
             {"name":"SHA256SUMS.txt","size":1,"browser_download_url":"{{Base}}SHA256SUMS.txt"}]}
            """;
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var service = new UpdateService(new HttpClient(handler), Repo, platform);

        var check = await service.CheckAsync(AppVersion.TryParse("0.1.0", out var current) ? current : AppVersion.Zero);

        Assert.Null(check.Error);
        Assert.NotNull(check.Decision?.Offer);
        Assert.Equal("https://api.github.com/repos/damanfra/CompleteUninstaller/releases/latest", handler.Requests.Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "privado")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    public async Task Check_explains_http_errors(HttpStatusCode status, string expected)
    {
        var service = new UpdateService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(status))), Repo,
            new PlatformPackage("a.zip", "a.exe", PackageKind.Zip));

        var check = await service.CheckAsync(AppVersion.Zero);

        Assert.Null(check.Decision);
        Assert.Contains(expected, check.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_survives_network_failure()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("sem rede"));
        var service = new UpdateService(new HttpClient(handler), Repo, new PlatformPackage("a.zip", "a.exe", PackageKind.Zip));

        var check = await service.CheckAsync(AppVersion.Zero);

        Assert.Contains("sem rede", check.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_style_replace_restores_the_old_file_when_the_move_fails()
    {
        var exe = Path.Combine(_dir, "app.exe");
        File.WriteAllText(exe, "antigo");

        // O arquivo novo não existe: o segundo Move falha e o antigo precisa voltar ao lugar.
        Assert.ThrowsAny<IOException>(() => SelfReplacer.Replace(exe, Path.Combine(_dir, "nao-existe"), windowsStyle: true));

        Assert.Equal("antigo", File.ReadAllText(exe));
    }

    [Fact]
    public void Unix_style_replace_overwrites_atomically()
    {
        var exe = Path.Combine(_dir, "app");
        var replacement = Path.Combine(_dir, "novo");
        File.WriteAllText(exe, "antigo");
        File.WriteAllText(replacement, "novo");

        SelfReplacer.Replace(exe, replacement, windowsStyle: false);

        Assert.Equal("novo", File.ReadAllText(exe));
        Assert.False(File.Exists(replacement));
    }
}
