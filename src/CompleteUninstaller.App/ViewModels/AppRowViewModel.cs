using System.Windows.Media;
using CompleteUninstaller.App.Mvvm;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.App.ViewModels;

/// <summary>Linha da lista de programas.</summary>
public sealed class AppRowViewModel : ObservableObject
{
    private ImageSource? _icon;

    public AppRowViewModel(InstalledApp app)
    {
        App = app;
        Tags = BuildTags(app);
        IsMicrosoft = AppClassifier.IsMicrosoft(app);
        IsWindowsComponent = AppClassifier.IsWindowsComponent(app);
    }

    public InstalledApp App { get; }

    /// <summary>Ícone carregado em segundo plano depois que a lista aparece.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool IsMicrosoft { get; }

    public bool IsWindowsComponent { get; }

    public string Name => App.DisplayName;

    public string Version => App.DisplayVersion ?? string.Empty;

    public string Publisher => App.Publisher ?? string.Empty;

    public string Size => ByteSize.Format(App.EstimatedSizeBytes);

    public long SortSize => App.EstimatedSizeBytes ?? -1;

    public DateTime? InstallDate => App.InstallDate;

    public string Source => App.SourceText;

    public string Tags { get; }

    public string InstallLocation => App.InstallLocation ?? "—";

    public string UninstallString => App.UninstallString ?? "—";

    public string QuietUninstallString => App.QuietUninstallString ?? "—";

    public string RegistryKey => App.UninstallKey?.ToString() ?? "—";

    public string ProductCode => App.MsiProductCode ?? "—";

    public string PackageName => App.PackageFullName ?? "—";

    public bool Matches(string search) =>
        Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Publisher.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    private static string BuildTags(InstalledApp app)
    {
        var tags = new List<string>();
        if (app.IsNotListedByWindows)
        {
            tags.Add(app.UserSid is null ? "Oculto no Windows" : "Oculto (outro usuário)");
        }

        if (app.IsSystemComponent)
        {
            tags.Add("Componente de sistema");
        }

        if (AppClassifier.IsWindowsComponent(app))
        {
            tags.Add("Parte do Windows");
        }

        if (app.IsUpdate)
        {
            tags.Add("Atualização");
        }

        if (app.Flags.HasFlag(AppFlags.NoRemove))
        {
            tags.Add("Remoção bloqueada");
        }

        if (app.Source != AppSource.Store && string.IsNullOrWhiteSpace(app.UninstallString) && app.MsiProductCode is null)
        {
            tags.Add("Sem desinstalador");
        }

        return string.Join(" · ", tags);
    }
}
