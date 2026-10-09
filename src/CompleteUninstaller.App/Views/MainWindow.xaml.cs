using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CompleteUninstaller.App.ViewModels;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.App.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly MainViewModel _viewModel;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _viewModel = new MainViewModel(services);
        _viewModel.UninstallRequested += OnUninstallRequested;
        _viewModel.QuarantineRequested += OnQuarantineRequested;
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

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
