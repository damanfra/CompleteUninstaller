using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CompleteUninstaller.App.Avalonia.Mvvm;
using CompleteUninstaller.App.Avalonia.Views;
using CompleteUninstaller.Infrastructure.Linux.Logging;

namespace CompleteUninstaller.App.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Info("Complete Uninstaller (Linux) iniciado.");
            CompleteUninstaller.Updater.UpdateService.CleanupOldVersion();
            var services = AppServices.Create();
            var window = new MainWindow(services);
            desktop.MainWindow = window;

            // Exceções não tratadas viram uma mensagem e uma entrada no log.
            AsyncRelayCommand.UnhandledException += ex => Dispatcher.UIThread.Post(async () =>
            {
                Log.Error("Erro não tratado", ex);
                await MessageDialog.ShowAsync(window,
                    $"Ocorreu um erro inesperado:\n\n{ex.Message}\n\nDetalhes foram gravados no log.");
            });

            if (services.Environment.IsRoot)
            {
                Dispatcher.UIThread.Post(async () => await MessageDialog.ShowAsync(window,
                    "O aplicativo está rodando como root. Funciona, mas o recomendado é abri-lo como usuário comum: " +
                    "ele pede a senha só quando precisa mexer em arquivos do sistema."));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
