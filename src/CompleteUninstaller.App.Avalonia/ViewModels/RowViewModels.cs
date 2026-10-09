using Avalonia.Media;
using CompleteUninstaller.App.Avalonia.Mvvm;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.App.Avalonia.ViewModels;

/// <summary>Linha da lista de programas.</summary>
public sealed class AppRowViewModel : ObservableObject
{
    private IImage? _icon;

    public AppRowViewModel(InstalledApp app)
    {
        App = app;
        Tags = BuildTags(app);
        RemoveCommandText = LinuxUninstallCommands.Build(app) is { Remove: { } remove } ? remove.ToString() : "—";
    }

    public InstalledApp App { get; }

    /// <summary>Ícone carregado em segundo plano depois que a lista aparece.</summary>
    public IImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public string Name => App.DisplayName;

    public string Version => App.DisplayVersion ?? string.Empty;

    public string Publisher => App.Publisher ?? string.Empty;

    public string Size => ByteSize.Format(App.EstimatedSizeBytes);

    public long SortSize => App.EstimatedSizeBytes ?? -1;

    public string Source => App.SourceText;

    public string Tags { get; }

    public string InstallLocation => App.InstallLocation ?? "—";

    public string PackageName => App.PackageName ?? "—";

    public string DesktopFile => App.DesktopFile ?? "—";

    public string RemoveCommandText { get; }

    public bool Matches(string search) =>
        Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Publisher.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || PackageName.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    private static string BuildTags(InstalledApp app)
    {
        var tags = new List<string>();
        if (app.IsSystemComponent)
        {
            tags.Add("Componente do sistema");
        }

        if (app.IsLibrary)
        {
            tags.Add("Biblioteca ou dependência");
        }

        if (app.Flags.HasFlag(AppFlags.NoRemove))
        {
            tags.Add("Remoção bloqueada");
        }

        if (app.Flags.HasFlag(AppFlags.HasDesktopEntry))
        {
            tags.Add("Tem atalho no menu");
        }

        return string.Join(" · ", tags);
    }
}

public sealed class LeftoverItemViewModel : ObservableObject
{
    private bool _isSelected;

    public LeftoverItemViewModel(LeftoverItem item)
    {
        Item = item;

        // Só os itens de confiança alta vêm marcados; os de confiança média exigem decisão do usuário.
        _isSelected = item.Confidence == Confidence.High;
    }

    public LeftoverItem Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsHigh => Item.Confidence == Confidence.High;

    public string ConfidenceText => IsHigh ? "Alta" : "Média";

    public string KindText => Item.Kind switch
    {
        LeftoverKind.Folder => "Pasta",
        LeftoverKind.File => "Arquivo",
        LeftoverKind.Shortcut => "Atalho (.desktop)",
        LeftoverKind.Service => "Serviço (systemd)",
        _ => Item.Kind.ToString(),
    };

    public string Target => Item.Target;

    public string Reason => Item.Reason;

    public string Details => Item.Details ?? string.Empty;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Item.Details);

    public string Size => ByteSize.Format(Item.SizeBytes);

    public long SortSize => Item.SizeBytes ?? -1;
}
