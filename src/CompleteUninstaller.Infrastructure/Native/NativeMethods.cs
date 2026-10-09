using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CompleteUninstaller.Infrastructure.Native;

internal static class NativeMethods
{
    // ---------------------------------------------------------------- Erros comuns
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_ACCESS_DENIED = 5;
    public const uint ERROR_MORE_DATA = 234;
    public const uint ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_BAD_EXE_FORMAT = 193;
    public const int ERROR_ELEVATION_REQUIRED = 740;
    public const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

    // ---------------------------------------------------------------- Windows Installer (msi.dll)
    public const uint MSIINSTALLCONTEXT_USERMANAGED = 1;
    public const uint MSIINSTALLCONTEXT_USERUNMANAGED = 2;
    public const uint MSIINSTALLCONTEXT_MACHINE = 4;
    public const uint MSIINSTALLCONTEXT_ALL = 7;
    public const int INSTALLSTATE_DEFAULT = 5;

    /// <summary>SID "Todos": enumera produtos de todos os usuários (exige administrador).</summary>
    public const string SidEveryone = "s-1-1-0";

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiEnumProductsExW")]
    public static extern uint MsiEnumProductsEx(
        string? szProductCode,
        string? szUserSid,
        uint dwContext,
        uint dwIndex,
        StringBuilder szInstalledProductCode,
        out uint pdwInstalledContext,
        StringBuilder szSid,
        ref uint pcchSid);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetProductInfoExW")]
    public static extern uint MsiGetProductInfoEx(
        string szProductCode,
        string? szUserSid,
        uint dwContext,
        string szProperty,
        StringBuilder szValue,
        ref uint pcchValue);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiQueryProductStateW")]
    public static extern int MsiQueryProductState(string szProduct);

    // ---------------------------------------------------------------- Processos e Job Objects
    public const uint CREATE_SUSPENDED = 0x00000004;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const int JobObjectBasicAccountingInformation = 1;
    public const uint STILL_ACTIVE = 259;

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateJobObjectW")]
    public static extern SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryInformationJobObject(
        SafeJobHandle hJob,
        int jobObjectInformationClass,
        out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION lpJobObjectInformation,
        int cbJobObjectInformationLength,
        IntPtr lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    // ---------------------------------------------------------------- Arquivos
    public const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "MoveFileExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    // ---------------------------------------------------------------- Service Control Manager
    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_STOP = 0x0020;
    public const uint DELETE = 0x00010000;
    public const uint SERVICE_CONTROL_STOP = 0x00000001;
    public const uint SERVICE_STOPPED = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    public static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    public static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ControlService(IntPtr hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseServiceHandle(IntPtr hSCObject);
}

internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle()
        : base(true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
