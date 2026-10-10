using System.ComponentModel;
using System.Diagnostics;
using CompleteUninstaller.Infrastructure.Linux.Logging;

namespace CompleteUninstaller.App.Avalonia.Services;

/// <summary>Abre pastas no gerenciador de arquivos da área de trabalho (xdg-open).</summary>
public static class ShellOpen
{
    public static void Folder(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo)?.Dispose();
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"Não foi possível abrir {path} com o xdg-open: {ex.Message}");
        }
    }
}
