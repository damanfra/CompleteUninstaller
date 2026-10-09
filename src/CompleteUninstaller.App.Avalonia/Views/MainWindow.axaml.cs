using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CompleteUninstaller.App.Avalonia.ViewModels;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.App.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services = null!;
    private readonly MainViewModel _viewModel = null!;

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
        Opened += async (_, _) => await _viewModel.RefreshAsync();
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

    private void OpenInstallLocation_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OpenInstallLocationCommand.CanExecute(null))
        {
            _viewModel.OpenInstallLocationCommand.Execute(null);
        }
    }
}
