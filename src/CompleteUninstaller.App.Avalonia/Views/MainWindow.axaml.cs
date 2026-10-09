using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CompleteUninstaller.App.Avalonia.ViewModels;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Updater;

namespace CompleteUninstaller.App.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services = null!;
    private readonly MainViewModel _viewModel = null!;
    private readonly UpdateFlow _updates = null!;

    /// <summary>Construtor sem parâmetros exigido pelo carregador de AXAML (visualização/designer).</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(AppServices services)
        : this()
    {
        _services = services;
        _viewModel = new MainViewModel(services);
        _viewModel.UninstallRequested += app => _ = OpenUninstallAsync(app);
        _viewModel.QuarantineRequested += () => _ = OpenQuarantineAsync();
        DataContext = _viewModel;
        Title = $"Complete Uninstaller {UpdateService.CurrentVersion()}";
        _updates = new UpdateFlow(
            services.Updates,
            message => MessageDialog.AskAsync(this, message),
            message => MessageDialog.ShowAsync(this, message),
            _viewModel.SetStatus,
            Close,
            Log.Info);
        Opened += async (_, _) =>
        {
            await _viewModel.RefreshAsync();
            await _updates.RunAsync(userInitiated: false); // verificação silenciosa ao abrir
        };
    }

    private async Task OpenUninstallAsync(InstalledApp app)
    {
        var viewModel = new UninstallViewModel(_services, app)
        {
            Confirm = message => MessageDialog.AskAsync(this, message),
        };
        var window = new UninstallWindow { DataContext = viewModel };
        await window.ShowDialog(this);
        await _viewModel.RefreshAsync(); // a lista muda depois de uma desinstalação
    }

    private async Task OpenQuarantineAsync()
    {
        var viewModel = new QuarantineViewModel(_services.Quarantine)
        {
            Confirm = message => MessageDialog.AskAsync(this, message),
            Notify = message => MessageDialog.ShowAsync(this, message),
        };
        var window = new QuarantineWindow { DataContext = viewModel };
        await window.ShowDialog(this);
        await _viewModel.RefreshAsync();
    }

    private void AppsGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.UninstallCommand.CanExecute(null))
        {
            _viewModel.UninstallCommand.Execute(null);
        }
    }

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e) =>
        await _updates.RunAsync(userInitiated: true);

    private void OpenInstallLocation_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OpenInstallLocationCommand.CanExecute(null))
        {
            _viewModel.OpenInstallLocationCommand.Execute(null);
        }
    }
}
