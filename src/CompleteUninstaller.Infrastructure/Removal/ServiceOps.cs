using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Native;
using CompleteUninstaller.Infrastructure.Platform;
using Microsoft.Win32;

namespace CompleteUninstaller.Infrastructure.Removal;

/// <summary>Para, remove e recria serviços do Windows.</summary>
internal static class ServiceOps
{
    public static ServiceBackup? ReadBackup(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        if (key is null)
        {
            return null;
        }

        return new ServiceBackup
        {
            Name = serviceName,
            ImagePath = key.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty,
            DisplayName = key.GetValue("DisplayName") as string,
            Description = key.GetValue("Description") as string,
            ObjectName = key.GetValue("ObjectName") as string,
            Start = key.GetValue("Start") is int start ? start : 3,
            Type = key.GetValue("Type") is int type ? type : 0x10,
            DelayedAutoStart = key.GetValue("DelayedAutostart") is int delayed && delayed == 1,
        };
    }

    /// <summary>Para o serviço (até 30 s) e o exclui. Retorna false se a exclusão ficou pendente (serviço não parou).</summary>
    public static bool StopAndDelete(string serviceName)
    {
        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeMethods.OpenService(
                manager,
                serviceName,
                NativeMethods.SERVICE_STOP | NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.DELETE);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var stopped = true;
                if (NativeMethods.QueryServiceStatus(service, out var status) && status.dwCurrentState != NativeMethods.SERVICE_STOPPED)
                {
                    Log.Info($"Parando o serviço {serviceName}...");
                    NativeMethods.ControlService(service, NativeMethods.SERVICE_CONTROL_STOP, out _);
                    var watch = Stopwatch.StartNew();
                    while (watch.Elapsed < TimeSpan.FromSeconds(30)
                           && NativeMethods.QueryServiceStatus(service, out status)
                           && status.dwCurrentState != NativeMethods.SERVICE_STOPPED)
                    {
                        Thread.Sleep(500);
                    }

                    stopped = status.dwCurrentState == NativeMethods.SERVICE_STOPPED;
                }

                if (!NativeMethods.DeleteService(service))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != NativeMethods.ERROR_SERVICE_MARKED_FOR_DELETE)
                    {
                        throw new Win32Exception(error);
                    }

                    stopped = false;
                }

                Log.Info($"Serviço {serviceName} excluído{(stopped ? string.Empty : " (pendente até parar/reiniciar)")}.");
                return stopped;
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(manager);
        }
    }

    public static void Recreate(ServiceBackup backup)
    {
        var arguments = new List<string>
        {
            "create", backup.Name,
            "binPath=", backup.ImagePath,
            "type=", (backup.Type & 0x20) != 0 ? "share" : "own",
            "start=", StartMode(backup),
        };

        if (!string.IsNullOrWhiteSpace(backup.DisplayName) && !backup.DisplayName.StartsWith('@'))
        {
            arguments.Add("DisplayName=");
            arguments.Add(backup.DisplayName);
        }

        if (IsBuiltInServiceAccount(backup.ObjectName))
        {
            arguments.Add("obj=");
            arguments.Add(backup.ObjectName!);
        }

        var result = ProcessRunner.Run("sc.exe", arguments);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"sc.exe create falhou (código {result.ExitCode}): {result.Output}");
        }

        if (!string.IsNullOrWhiteSpace(backup.Description) && !backup.Description.StartsWith('@'))
        {
            ProcessRunner.Run("sc.exe", ["description", backup.Name, backup.Description]);
        }
    }

    private static string StartMode(ServiceBackup backup) => backup.Start switch
    {
        0 => "boot",
        1 => "system",
        2 => backup.DelayedAutoStart ? "delayed-auto" : "auto",
        4 => "disabled",
        _ => "demand",
    };

    /// <summary>Contas que não exigem senha para recriar o serviço.</summary>
    private static bool IsBuiltInServiceAccount(string? account) =>
        account is not null
        && (account.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)
            || account.Equals(@"NT AUTHORITY\LocalService", StringComparison.OrdinalIgnoreCase)
            || account.Equals(@"NT AUTHORITY\NetworkService", StringComparison.OrdinalIgnoreCase));
}
