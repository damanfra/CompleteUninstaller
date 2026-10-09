using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace CompleteUninstaller.Infrastructure.Native;

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass
{
}

/// <summary>IShellLinkW — a ordem dos métodos precisa ser exatamente a da vtable.</summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

    void GetIDList(out IntPtr ppidl);

    void SetIDList(IntPtr pidl);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out short pwHotkey);

    void SetHotkey(short wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);

    void Resolve(IntPtr hwnd, int fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

/// <summary>Lê o destino de arquivos .lnk sem resolvê-los (não dispara buscas nem diálogos).</summary>
internal static class ShellLinkReader
{
    private const uint SLGP_RAWPATH = 0x4;
    private const int STGM_READ = 0;

    public static string? TryGetTarget(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(lnkPath, STGM_READ);
            var buffer = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            var raw = buffer.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : Environment.ExpandEnvironmentVariables(raw);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }
}
