using System.Diagnostics;

namespace CompleteUninstaller.Infrastructure.Platform;

/// <summary>Executa ferramentas de linha de comando do Windows (sc.exe, schtasks.exe, powershell.exe) sem janela.</summary>
internal static class ProcessRunner
{
    public sealed record Result(int ExitCode, string Output);

    public static Result Run(string fileName, IEnumerable<string> arguments, int timeoutMs = 120_000)
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return new Result(-1, $"Tempo esgotado executando {fileName}.");
        }

        process.WaitForExit();
        var output = $"{stdout.GetAwaiter().GetResult()}{Environment.NewLine}{stderr.GetAwaiter().GetResult()}".Trim();
        return new Result(process.ExitCode, output);
    }
}
