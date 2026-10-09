using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.Core.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Mozilla Firefox (x64 pt-BR)", "mozilla firefox")]
    [InlineData("7-Zip 24.08 (x64)", "7 zip")]
    [InlineData("Python 3.12.1 (64-bit)", "python")]
    [InlineData("Notepad++ (64-bit x64)", "notepad++")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33130", "microsoft visual c++ 2015 2022 redistributable")]
    [InlineData("Ação Rápida v2.1", "acao rapida")]
    [InlineData("", "")]
    public void Normalize_removes_versions_architecture_locale_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, NameNormalizer.Normalize(input));

    [Theory]
    [InlineData("Microsoft Corporation", "microsoft")]
    [InlineData("Google LLC", "google")]
    [InlineData("Spotify AB", "spotify")]
    [InlineData("Empresa Exemplo Ltda.", "empresa exemplo")]
    public void NormalizePublisher_removes_corporate_suffixes(string input, string expected) =>
        Assert.Equal(expected, NameNormalizer.NormalizePublisher(input));
}

public class AppNameMatcherTests
{
    private readonly AppNameMatcher _firefox = new("Mozilla Firefox (x64 pt-BR)", "Mozilla");

    [Theory]
    [InlineData("Mozilla Firefox", NameMatch.Exact)]
    [InlineData("Firefox", NameMatch.Exact)]
    [InlineData("mozilla firefox", NameMatch.Exact)]
    [InlineData("Mozilla", NameMatch.Publisher)]
    [InlineData("Firefox Profiles", NameMatch.Partial)]
    [InlineData("Thunderbird", NameMatch.None)]
    [InlineData("Temp", NameMatch.None)]
    [InlineData("Common Files", NameMatch.None)]
    [InlineData("", NameMatch.None)]
    public void Firefox_matches(string folder, NameMatch expected) =>
        Assert.Equal(expected, _firefox.Match(folder));

    [Fact]
    public void Single_word_partial_only_if_first_or_last_word_of_the_name()
    {
        var vscode = new AppNameMatcher("Microsoft Visual Studio Code (User)", "Microsoft Corporation");

        Assert.Equal(NameMatch.Exact, vscode.Match("Visual Studio Code"));
        Assert.Equal(NameMatch.Partial, vscode.Match("Code"));
        Assert.Equal(NameMatch.None, vscode.Match("Studio"));

        // "Microsoft" é genérico demais: nunca vira candidato, nem como pasta de fabricante.
        Assert.Equal(NameMatch.None, vscode.Match("Microsoft"));
    }

    [Fact]
    public void Punctuation_and_spacing_do_not_matter()
    {
        var sevenZip = new AppNameMatcher("7-Zip 24.08 (x64)", "Igor Pavlov");

        Assert.Equal(NameMatch.Exact, sevenZip.Match("7-Zip"));
        Assert.Equal(NameMatch.Exact, sevenZip.Match("7Zip"));
    }

    [Fact]
    public void Generic_folders_never_match()
    {
        var matcher = new AppNameMatcher("Update Manager", "Foo Software");

        Assert.Equal(NameMatch.None, matcher.Match("Update"));
        Assert.Equal(NameMatch.None, matcher.Match("Cache"));
        Assert.Equal(NameMatch.None, matcher.Match("Logs"));
    }

    [Fact]
    public void Different_versions_folder_is_not_an_exact_match()
    {
        var python = new AppNameMatcher("Python 3.12.1 (64-bit)", "Python Software Foundation");

        Assert.Equal(NameMatch.Exact, python.Match("Python"));
        Assert.Equal(NameMatch.None, python.Match("Python311"));
    }

    [Fact]
    public void Alternative_names_become_exact_matches()
    {
        var matcher = new AppNameMatcher("Some Tool Suite", "ACME", ["SomeToolSuite"]);

        Assert.Equal(NameMatch.Exact, matcher.Match("SomeToolSuite"));
    }
}
