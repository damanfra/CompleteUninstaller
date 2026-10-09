using System.ComponentModel;
using System.Windows;
using CompleteUninstaller.App.ViewModels;
using CompleteUninstaller.Core.Models;

namespace CompleteUninstaller.App.Views;

public partial class UninstallWindow : Window
{
    private readonly UninstallViewModel _viewModel;

    public UninstallWindow(AppServices services, InstalledApp app)
    {
        InitializeComponent();
        _viewModel = new UninstallViewModel(services, app) { Confirm = AskYesNo };
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private bool AskYesNo(string message) =>
        MessageBox.Show(this, message, "Complete Uninstaller", MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsWorkingStep)
        {
            MessageBox.Show(this,
                _viewModel.IsWaitingUninstaller
                    ? "O desinstalador ainda está em execução. Se ele já terminou, clique em \"O desinstalador já terminou\"."
                    : "Aguarde a etapa atual terminar.",
                "Complete Uninstaller", MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
