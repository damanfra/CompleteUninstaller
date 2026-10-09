namespace CompleteUninstaller.Core.Models;

public enum AppSource
{
    /// <summary>Chave em ...\CurrentVersion\Uninstall (o que o Painel de Controle lê).</summary>
    Registry,

    /// <summary>Produto MSI encontrado pela API do Windows Installer e sem entrada visível no Registro.</summary>
    WindowsInstaller,

    /// <summary>Pacote AppX/MSIX (Microsoft Store e afins).</summary>
    Store,
}

public enum RegistryScope
{
    Machine64,
    Machine32,
    User,
}

[Flags]
public enum AppFlags
{
    None = 0,

    /// <summary>SystemComponent=1: o Painel de Controle esconde.</summary>
    SystemComponent = 1,

    /// <summary>Atualização/hotfix de outro produto.</summary>
    Update = 2,

    /// <summary>NoRemove=1: o fabricante desabilitou a remoção.</summary>
    NoRemove = 4,

    /// <summary>Não aparece em nenhuma lista do Windows (ex.: MSI sem chave Uninstall).</summary>
    NotListedByWindows = 8,

    /// <summary>Pacote da Store pré-instalado com o Windows (provisionado na imagem do sistema).</summary>
    Provisioned = 16,
}

/// <summary>Localização de uma chave de desinstalação no Registro.</summary>
/// <param name="Hive">"HKLM" ou "HKU".</param>
/// <param name="Is32BitView">Aberta pela visão de 32 bits (WOW6432Node) do HKLM.</param>
/// <param name="SubKeyPath">Caminho relativo à colmeia (no HKU começa com o SID).</param>
public sealed record RegistryLocation(string Hive, bool Is32BitView, string SubKeyPath)
{
    public override string ToString()
    {
        var root = Hive == "HKU" ? "HKEY_USERS" : "HKEY_LOCAL_MACHINE";
        var path = SubKeyPath;
        if (Is32BitView && path.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase))
        {
            path = @"SOFTWARE\WOW6432Node\" + path[@"SOFTWARE\".Length..];
        }

        return $@"{root}\{path}";
    }
}

/// <summary>Um programa instalado, de qualquer origem.</summary>
public sealed class InstalledApp
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required AppSource Source { get; init; }

    public RegistryScope? Scope { get; init; }

    public string? DisplayVersion { get; set; }

    public string? Publisher { get; set; }

    public string? InstallLocation { get; set; }

    public DateTime? InstallDate { get; set; }

    public long? EstimatedSizeBytes { get; set; }

    public string? UninstallString { get; set; }

    public string? QuietUninstallString { get; set; }

    public string? DisplayIcon { get; set; }

    /// <summary>Caminho do instalador em cache (pacotes "Burn"/WiX).</summary>
    public string? BundleCachePath { get; set; }

    public bool IsWindowsInstaller { get; set; }

    public string? MsiProductCode { get; set; }

    public RegistryLocation? UninstallKey { get; init; }

    /// <summary>SID do usuário, para instalações por usuário.</summary>
    public string? UserSid { get; set; }

    public string? PackageFullName { get; init; }

    public string? PackageFamilyName { get; init; }

    /// <summary>Logotipo declarado pelo pacote da Store (pode não existir no tamanho exato; ver IconLocator).</summary>
    public string? LogoPath { get; init; }

    public AppFlags Flags { get; set; }

    public bool IsSystemComponent => Flags.HasFlag(AppFlags.SystemComponent);

    public bool IsUpdate => Flags.HasFlag(AppFlags.Update);

    public bool IsNotListedByWindows => Flags.HasFlag(AppFlags.NotListedByWindows);

    public bool HasQuietUninstall =>
        !string.IsNullOrWhiteSpace(QuietUninstallString) || IsWindowsInstaller || MsiProductCode is not null;

    public string SourceText => Source switch
    {
        AppSource.Registry => Scope switch
        {
            RegistryScope.Machine32 => "Registro (32 bits)",
            RegistryScope.User => "Registro (usuário)",
            _ => "Registro",
        },
        AppSource.WindowsInstaller => "Windows Installer",
        AppSource.Store => "Store / MSIX",
        _ => Source.ToString(),
    };

    public override string ToString() => $"{DisplayName} {DisplayVersion}".Trim();
}
