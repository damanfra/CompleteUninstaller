using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CompleteUninstaller.App.Mvvm;
using CompleteUninstaller.App.Services;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Updater;
using CompleteUninstaller.Infrastructure.Inventory;

namespace CompleteUninstaller.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _searchText = string.Empty;
    private bool _showSystemComponents;
    private bool _showUpdates;
    private bool _showStoreApps = true;
    private bool _hideWindowsItems = true;
    private bool _hideMicrosoftApps;
    private CancellationTokenSource? _iconLoading;
    private bool _isBusy;
    private string _statusText = "Carregando...";
    private AppRowViewModel? _selectedApp;
    private IReadOnlyList<string> _lastErrors = Array.Empty<string>();

    public MainViewModel(AppServices services)
    {
        _services = services;
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = Filter;
        AppsView.SortDescriptions.Add(new SortDescription(nameof(AppRowViewModel.Name), ListSortDirection.Ascending));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        UninstallCommand = new RelayCommand(
            () => UninstallRequested?.Invoke(SelectedApp!.App),
            () => SelectedApp is not null && !IsBusy);
        OpenQuarantineCommand = new RelayCommand(() => QuarantineRequested?.Invoke());
        OpenInstallLocationCommand = new RelayCommand(OpenInstallLocation, CanOpenInstallLocation);
        OpenLogCommand = new RelayCommand(OpenLogFolder);
    }

    public event Action<InstalledApp>? UninstallRequested;

    public event Action? QuarantineRequested;

    public ObservableCollection<AppRowViewModel> Apps { get; } = [];

    public ICollectionView AppsView { get; }

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

    public bool ShowUpdates
    {
        get => _showUpdates;
        set
        {
            if (SetProperty(ref _showUpdates, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowStoreApps
    {
        get => _showStoreApps;
        set
        {
            if (SetProperty(ref _showStoreApps, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool HideWindowsItems
    {
        get => _hideWindowsItems;
        set
        {
            if (SetProperty(ref _hideWindowsItems, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool HideMicrosoftApps
    {
        get => _hideMicrosoftApps;
        set
        {
            if (SetProperty(ref _hideMicrosoftApps, value))
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
                CommandManager.InvalidateRequerySuggested();
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
            }
        }
    }

    public bool HasSelection => SelectedApp is not null;

    public bool HasNoSelection => SelectedApp is null;

    /// <summary>Versão do executável (carimbada pela Action a partir da tag), mostrada na barra de status.</summary>
    public string VersionText => $"v{UpdateService.CurrentVersion()}";

    public void SetStatus(string text) => StatusText = text;

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Lendo programas instalados (Registro, Windows Installer e Store)...";
        try
        {
            var selectedId = SelectedApp?.App.Id;
            var result = await _services.Inventory.LoadAsync();

            // Não usar AppsView.DeferRefresh() aqui: a DataGrid mexe na seleção (CurrentItem) durante
            // as alterações e o WPF lança InvalidOperationException enquanto o refresh está adiado.
            SelectedApp = null;
            Apps.Clear();
            foreach (var app in result.Apps)
            {
                Apps.Add(new AppRowViewModel(app));
            }

            SelectedApp = selectedId is null ? null : Apps.FirstOrDefault(a => a.App.Id == selectedId);
            _lastErrors = result.Errors;
            UpdateStatus();
            StartIconLoading();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool Filter(object item)
    {
        if (item is not AppRowViewModel row)
        {
            return false;
        }

        var app = row.App;
        if (!ShowSystemComponents && app.IsSystemComponent)
        {
            return false;
        }

        if (!ShowUpdates && app.IsUpdate)
        {
            return false;
        }

        if (!ShowStoreApps && app.Source == AppSource.Store)
        {
            return false;
        }

        if (HideWindowsItems && row.IsWindowsComponent)
        {
            return false;
        }

        if (HideMicrosoftApps && row.IsMicrosoft)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText) || row.Matches(SearchText.Trim());
    }

    /// <summary>Carrega os ícones em segundo plano, sem travar a lista; cancela a carga anterior.</summary>
    private void StartIconLoading()
    {
        _iconLoading?.Cancel();
        var cancellation = new CancellationTokenSource();
        _iconLoading = cancellation;

        var rows = Apps.ToList();
        var dispatcher = Application.Current.Dispatcher;
        _ = Task.Run(() =>
        {
            foreach (var row in rows)
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                var icon = IconLoader.Load(IconLocator.Locate(row.App));
                dispatcher.InvokeAsync(() => row.Icon = icon);
            }
        });
    }

    private void ApplyFilter()
    {
        AppsView.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var visible = AppsView.Cast<object>().Count();
        var hiddenByWindows = Apps.Count(a => a.App.IsNotListedByWindows || a.App.IsSystemComponent);
        var status = $"{visible} exibidos · {Apps.Count} encontrados · {hiddenByWindows} que o Painel de Controle não mostra";
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
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private static void OpenLogFolder()
    {
        var folder = Infrastructure.AppPaths.LogsRoot;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
