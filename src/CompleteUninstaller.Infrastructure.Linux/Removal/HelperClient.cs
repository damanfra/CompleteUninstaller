using System.Text.Json;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

/// <summary>Chama o auxiliar com privilégios: o próprio executável reiniciado como <c>pkexec &lt;app&gt; --helper</c>.</summary>
public sealed class HelperClient
{
    private readonly LinuxEnvironment _environment;

    public HelperClient(LinuxEnvironment environment) => _environment = environment;

    public HelperResponse Send(HelperRequest request)
    {
        if (_environment.IsRoot)
        {
            return PrivilegedHelper.Execute(request);
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            return new HelperResponse { Ok = false, Error = "Não foi possível localizar o executável do aplicativo." };
        }

        // Executando com "dotnet app.dll": o auxiliar precisa do mesmo comando.
        var arguments = new List<string> { processPath };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.Ordinal))
        {
            arguments.Add(Environment.GetCommandLineArgs()[0]);
        }

        arguments.Add("--helper");
        Log.Info($"Chamando o auxiliar com privilégios ({request.Op}, {request.Items.Count} item(ns)).");
        var result = CommandRunner.Run("pkexec", arguments, timeoutMs: 1_800_000, stdin: HelperProtocol.Serialize(request));
        if (result.ExitCode is 126 or 127 && string.IsNullOrWhiteSpace(result.Output))
        {
            return new HelperResponse { Ok = false, Error = "A autenticação foi cancelada." };
        }

        try
        {
            var response = JsonSerializer.Deserialize<HelperResponse>(result.Output, HelperProtocol.Json);
            if (response is not null)
            {
                return response;
            }
        }
        catch (JsonException)
        {
        }

        var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        Log.Warn($"Resposta inesperada do auxiliar (código {result.ExitCode}): {detail}");
        return new HelperResponse { Ok = false, Error = $"O auxiliar com privilégios falhou: {detail.Trim()}" };
    }
}
