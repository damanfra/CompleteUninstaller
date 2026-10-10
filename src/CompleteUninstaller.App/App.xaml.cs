using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using CompleteUninstaller.App.Mvvm;
using CompleteUninstaller.Infrastructure.Logging;

namespace CompleteUninstaller.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tema Fluent nativo do WPF (.NET 9+), seguindo o modo claro/escuro do Windows.
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AsyncRelayCommand.UnhandledException += ShowError;

        Log.Info("Complete Uninstaller iniciado.");
        Updater.UpdateService.CleanupOldVersion(); // apaga o executável antigo deixado por uma atualização
        if (!IsAdministrator())
        {
            MessageBox.Show(
                "O Complete Uninstaller precisa ser executado como administrador para listar e remover tudo corretamente.",
                "Complete Uninstaller",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var services = AppServices.Create();
        var window = new Views.MainWindow(services);
        MainWindow = window;
        window.Show();
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private static void ShowError(Exception exception)
    {
        Log.Error("Erro não tratado", exception);
        MessageBox.Show(
            $"Ocorreu um erro inesperado:\n\n{exception.Message}\n\nDetalhes foram gravados no log.",
            "Complete Uninstaller",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
