using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CompleteUninstaller.App.Avalonia.Mvvm;
using CompleteUninstaller.App.Avalonia.Services;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Linux;
using CompleteUninstaller.Infrastructure.Linux.Inventory;
using CompleteUninstaller.Infrastructure.Linux.Logging;

namespace CompleteUninstaller.App.Avalonia.ViewModels;

public sealed record SourceFilter(string Label, AppSource? Source)
{
    public override string ToString() => Label;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly List<AppRowViewModel> _all = [];
    private string _searchText = string.Empty;
    private bool _showLibraries;
    private bool _showSystemComponents;
    private SourceFilter _selectedSource;
    private CancellationTokenSource? _iconLoading;
    private bool _isBusy;
    private string _statusText = "Carregando...";
    private AppRowViewModel? _selectedApp;
    private IReadOnlyList<string> _lastErrors = Array.Empty<string>();

    public MainViewModel(AppServices services)
    {
        _services = services;
        Sources =
        [
            new SourceFilter("Todas as origens", null),
            new SourceFilter("apt (.deb)", AppSource.Dpkg),
            new SourceFilter("dnf (.rpm)", AppSource.Rpm),
            new SourceFilter("Flatpak", AppSource.Flatpak),
            new SourceFilter("Snap", AppSource.Snap),
        ];
        _selectedSource = Sources[0];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        UninstallCommand = new RelayCommand(
            () => UninstallRequested?.Invoke(SelectedApp!.App),
            () => SelectedApp is not null && !IsBusy && !SelectedApp.App.Flags.HasFlag(AppFlags.NoRemove));
        OpenQuarantineCommand = new RelayCommand(() => QuarantineRequested?.Invoke());
        OpenInstallLocationCommand = new RelayCommand(OpenInstallLocation, CanOpenInstallLocation);
        OpenLogCommand = new RelayCommand(OpenLogFolder);
    }

    public event Action<InstalledApp>? UninstallRequested;

    public event Action? QuarantineRequested;

    public IReadOnlyList<SourceFilter> Sources { get; }

    /// <summary>Linhas visíveis (já filtradas).</summary>
    public ObservableCollection<AppRowViewModel> Apps { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand UninstallCommand { get; }

    public ICommand OpenQuarantineCommand { get; }

    public ICommand OpenInstallLocationCommand { get; }

    public ICommand OpenLogCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowLibraries
    {
        get => _showLibraries;
        set
        {
            if (SetProperty(ref _showLibraries, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowSystemComponents
    {
        get => _showSystemComponents;
        set
        {
            if (SetProperty(ref _showSystemComponents, value))
            {
                ApplyFilter();
            }
        }
    }

    public SourceFilter SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandRequery.Invalidate();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public AppRowViewModel? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (SetProperty(ref _selectedApp, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                CommandRequery.Invalidate();
            }
        }
    }

    public bool HasSelection => SelectedApp is not null;

    public bool HasNoSelection => SelectedApp is null;

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Lendo programas instalados (apt, dnf, Flatpak e Snap)...";
        try
        {
            var selectedId = SelectedApp?.App.Id;
            var result = await _services.Inventory.LoadAsync();

            _all.Clear();
            _all.AddRange(result.Apps.Select(a => new AppRowViewModel(a)));
            _lastErrors = result.Errors;
            ApplyFilter(selectedId);
            StartIconLoading();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool Visible(AppRowViewModel row)
    {
        var app = row.App;
        if (!ShowSystemComponents && app.IsSystemComponent)
        {
            return false;
        }

        if (!ShowLibraries && app.IsLibrary)
        {
            return false;
        }

        if (SelectedSource.Source is { } source && app.Source != source)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText) || row.Matches(SearchText.Trim());
    }

    private void ApplyFilter(string? keepSelectedId = null)
    {
        var selectedId = keepSelectedId ?? SelectedApp?.App.Id;
        SelectedApp = null;
        Apps.Clear();
        foreach (var row in _all.Where(Visible))
        {
            Apps.Add(row);
        }

        SelectedApp = selectedId is null ? null : Apps.FirstOrDefault(a => a.App.Id == selectedId);
        UpdateStatus();
    }

    /// <summary>Carrega os ícones em segundo plano, sem travar a lista; cancela a carga anterior.</summary>
    private void StartIconLoading()
    {
        _iconLoading?.Cancel();
        var cancellation = new CancellationTokenSource();
        _iconLoading = cancellation;

        var rows = _all.Where(r => r.App.IconName is not null).ToList();
        var environment = _services.Environment;
        _ = Task.Run(() =>
        {
            foreach (var row in rows)
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                var path = DesktopCatalog.FindIcon(row.App.IconName, environment);
                if (path is null)
                {
                    continue;
                }

                try
                {
                    var bitmap = new Bitmap(path);
                    Dispatcher.UIThread.Post(() => row.Icon = bitmap);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
                {
                    Log.Info($"Ícone ignorado ({path}): {ex.Message}");
                }
            }
        });
    }

    private void UpdateStatus()
    {
        var hiddenLibraries = _all.Count(a => a.App.IsLibrary || a.App.IsSystemComponent) - Apps.Count(a => a.App.IsLibrary || a.App.IsSystemComponent);
        var status = $"{Apps.Count} exibidos · {_all.Count} encontrados · {Math.Max(hiddenLibraries, 0)} bibliotecas e componentes de sistema ocultos";
        if (_lastErrors.Count > 0)
        {
            status += $" · Avisos: {string.Join(" | ", _lastErrors)}";
        }

        StatusText = status;
    }

    private bool CanOpenInstallLocation() =>
        SelectedApp?.App.InstallLocation is { Length: > 0 } path && Directory.Exists(path);

    private void OpenInstallLocation()
    {
        if (SelectedApp?.App.InstallLocation is { } path)
        {
            ShellOpen.Folder(path);
        }
    }

    private static void OpenLogFolder()
    {
        var folder = AppPaths.LogsRoot;
        Directory.CreateDirectory(folder);
        ShellOpen.Folder(folder);
    }
}
