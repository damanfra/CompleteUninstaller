using Avalonia;
using CompleteUninstaller.Infrastructure.Linux.Removal;

namespace CompleteUninstaller.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // "--helper": modo sem janela, iniciado pelo pkexec, que move itens do sistema para a quarentena.
        if (args.Contains("--helper", StringComparer.Ordinal))
        {
            return PrivilegedHelper.Run(Console.In, Console.Out);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
