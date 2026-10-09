using System.ComponentModel;
using System.Diagnostics;
using CompleteUninstaller.Core.Linux;

namespace CompleteUninstaller.Infrastructure.Linux.Platform;

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Executa ferramentas de linha de comando sem shell (cada argumento vai separado) e com
/// LC_ALL=C, para que a saída tenha sempre o mesmo formato, em qualquer idioma do sistema.
/// </summary>
public static class CommandRunner
{
    public static string? Find(string program)
    {
        if (program.Contains('/', StringComparison.Ordinal))
        {
            return File.Exists(program) ? program : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin";
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries).Concat(["/usr/sbin", "/sbin"]))
        {
            var candidate = Path.Combine(dir, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool Exists(string program) => Find(program) is not null;

    public static CommandResult Run(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        int timeoutMs = 120_000,
        string? stdin = null)
    {
        var startInfo = CreateStartInfo(fileName, arguments, environment);
        startInfo.RedirectStandardInput = stdin is not null;

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return new CommandResult(-1, string.Empty, $"Tempo esgotado executando {fileName}.");
            }

            process.WaitForExit();
            return new CommandResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        catch (Win32Exception ex)
        {
            return new CommandResult(-1, string.Empty, $"Não foi possível executar {fileName}: {ex.Message}");
        }
    }

    /// <summary>Executa mostrando cada linha de saída à medida que aparece. Devolve o código de saída.</summary>
    public static async Task<int> RunStreamingAsync(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(fileName, arguments, environment);
        using var process = new Process { StartInfo = startInfo };
        void Handler(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is { Length: > 0 } line)
            {
                onLine(line);
            }
        }

        process.OutputDataReceived += Handler;
        process.ErrorDataReceived += Handler;
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            onLine($"Não foi possível executar {fileName}: {ex.Message}");
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        return process.ExitCode;
    }

    /// <summary>Embrulha o comando no pkexec quando ele precisa de root e o aplicativo não está como root.</summary>
    public static (string FileName, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string>? Environment)
        Elevate(LinuxCommand command, bool isRoot)
    {
        if (!command.NeedsRoot || isRoot)
        {
            return (command.FileName, command.Arguments, command.Environment);
        }

        // O pkexec limpa o ambiente; variáveis necessárias passam pelo "env".
        var arguments = new List<string> { "env" };
        if (command.Environment is not null)
        {
            arguments.AddRange(command.Environment.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        arguments.Add(command.FileName);
        arguments.AddRange(command.Arguments);
        return ("pkexec", arguments, null);
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }
}
