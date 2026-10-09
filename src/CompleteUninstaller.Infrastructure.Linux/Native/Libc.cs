using System.Runtime.InteropServices;

namespace CompleteUninstaller.Infrastructure.Linux.Native;

/// <summary>P/Invoke mínimo da libc (geteuid), escrito à mão como no restante do projeto.</summary>
internal static class Libc
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    public static bool IsRoot()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return GetEffectiveUserId() == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return string.Equals(Environment.UserName, "root", StringComparison.Ordinal);
        }
    }
}
