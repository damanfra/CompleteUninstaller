using Avalonia.Controls;
using Avalonia.Interactivity;
using CompleteUninstaller.App.Avalonia.ViewModels;

namespace CompleteUninstaller.App.Avalonia.Views;

public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Fechar no meio da operação deixaria o pacote removido sem a limpeza (ou o contrário).
        if (DataContext is UninstallViewModel { IsWorkingStep: true })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
