using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Native;

namespace CompleteUninstaller.Infrastructure.Uninstall;

/// <summary>
/// Inicia o desinstalador dentro de um Job Object e permite saber quando TODOS os processos
/// filhos terminaram. Necessário porque Inno Setup, NSIS e outros se copiam para a pasta Temp,
/// relançam a cópia e encerram o processo original na hora.
/// </summary>
internal sealed class TrackedProcess : IDisposable
{
    private readonly SafeJobHandle _job;
    private readonly bool _assigned;
    private IntPtr _processHandle;
    private Process? _shellProcess;

    private TrackedProcess(SafeJobHandle job, IntPtr processHandle, Process? shellProcess, bool assigned)
    {
        _job = job;
        _processHandle = processHandle;
        _shellProcess = shellProcess;
        _assigned = assigned;
    }

    /// <summary>Quantidade de processos ainda vivos da árvore do desinstalador.</summary>
    public int ActiveProcessCount
    {
        get
        {
            if (_assigned && NativeMethods.QueryInformationJobObject(
                    _job,
                    NativeMethods.JobObjectBasicAccountingInformation,
                    out var info,
                    Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(),
                    IntPtr.Zero))
            {
                return (int)info.ActiveProcesses;
            }

            return HasMainProcessExited ? 0 : 1;
        }
    }

    public int? MainExitCode
    {
        get
        {
            if (_shellProcess is not null)
            {
                return _shellProcess.HasExited ? _shellProcess.ExitCode : null;
            }

            if (_processHandle != IntPtr.Zero && NativeMethods.GetExitCodeProcess(_processHandle, out var code)
                && code != NativeMethods.STILL_ACTIVE)
            {
                return unchecked((int)code);
            }

            return null;
        }
    }

    private bool HasMainProcessExited => MainExitCode is not null;

    public static TrackedProcess Start(string commandLine, string? workingDirectory)
    {
        var job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var startupInfo = new NativeMethods.STARTUPINFO { cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>() };
            var buffer = new StringBuilder(commandLine, commandLine.Length + 1);
            var created = NativeMethods.CreateProcess(
                null,
                buffer,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.CREATE_SUSPENDED | NativeMethods.CREATE_UNICODE_ENVIRONMENT,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInfo);

            if (created)
            {
                var assigned = NativeMethods.AssignProcessToJobObject(job, processInfo.hProcess);
                if (!assigned)
                {
                    Log.Warn($"Não foi possível associar o desinstalador ao Job Object (erro {Marshal.GetLastWin32Error()}).");
                }

                NativeMethods.ResumeThread(processInfo.hThread);
                NativeMethods.CloseHandle(processInfo.hThread);
                return new TrackedProcess(job, processInfo.hProcess, null, assigned);
            }

            var error = Marshal.GetLastWin32Error();
            if (error is NativeMethods.ERROR_BAD_EXE_FORMAT or NativeMethods.ERROR_FILE_NOT_FOUND
                or NativeMethods.ERROR_PATH_NOT_FOUND or NativeMethods.ERROR_ELEVATION_REQUIRED)
            {
                // Não é um executável direto (ex.: .msi, .bat, associação de arquivo): usa o shell.
                return StartWithShell(job, commandLine, workingDirectory, error);
            }

            throw new Win32Exception(error);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    private static TrackedProcess StartWithShell(SafeJobHandle job, string commandLine, string? workingDirectory, int originalError)
    {
        var parsed = CommandLineParser.Parse(commandLine, File.Exists) ?? throw new Win32Exception(originalError);
        var startInfo = new ProcessStartInfo(parsed.FileName, parsed.Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };

        var process = Process.Start(startInfo) ?? throw new Win32Exception(originalError);
        var assigned = false;
        try
        {
            assigned = NativeMethods.AssignProcessToJobObject(job, process.Handle);
        }
        catch (InvalidOperationException)
        {
            // O processo já terminou.
        }

        return new TrackedProcess(job, IntPtr.Zero, process, assigned);
    }

    public void Dispose()
    {
        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }

        _shellProcess?.Dispose();
        _shellProcess = null;

        // Fechar o handle do job NÃO encerra os processos (não usamos KILL_ON_JOB_CLOSE).
        _job.Dispose();
    }
}
