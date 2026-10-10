using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CompleteUninstaller.App.ViewModels;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Updater;

namespace CompleteUninstaller.App.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly MainViewModel _viewModel;
    private readonly UpdateFlow _updates;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _viewModel = new MainViewModel(services);
        _viewModel.UninstallRequested += OnUninstallRequested;
        _viewModel.QuarantineRequested += OnQuarantineRequested;
        DataContext = _viewModel;
        Title = $"Complete Uninstaller {UpdateService.CurrentVersion()}";
        _updates = new UpdateFlow(
            services.Updates,
            message => Task.FromResult(MessageBox.Show(this, message, "Atualização", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes),
            message =>
            {
                MessageBox.Show(this, message, "Atualização", MessageBoxButton.OK, MessageBoxImage.Information);
                return Task.CompletedTask;
            },
            _viewModel.SetStatus,
            Close,
            Log.Info);
        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshAsync();
            await _updates.RunAsync(userInitiated: false); // verificação silenciosa ao abrir
        };
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        await _updates.RunAsync(userInitiated: true);

    private async void OnUninstallRequested(InstalledApp app)
    {
        var window = new UninstallWindow(_services, app) { Owner = this };
        window.ShowDialog();
        await _viewModel.RefreshAsync();
    }

    private void OnQuarantineRequested()
    {
        var window = new QuarantineWindow(_services) { Owner = this };
        window.ShowDialog();
    }

    private void AppsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Só reage a duplo clique em uma linha (não no cabeçalho nem na barra de rolagem).
        var isRow = e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(AppsGrid, source) is DataGridRow;
        if (isRow && _viewModel.UninstallCommand.CanExecute(null))
        {
            _viewModel.UninstallCommand.Execute(null);
        }
    }
}
