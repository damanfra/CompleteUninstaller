using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

/// <summary>
/// Move arquivos e pastas com o <c>mv</c> do sistema: no mesmo sistema de arquivos é um "renomear" atômico;
/// entre dispositivos copia preservando dono, permissões e links, e só então apaga o original. Isso mantém
/// as permissões intactas, o que o File.Move do .NET não garante, e a restauração depende disso.
/// </summary>
public static class FileMover
{
    /// <summary>Move <paramref name="source"/> para <paramref name="destination"/>, que ainda não pode existir.</summary>
    public static void Move(string source, string destination)
    {
        if (!FileSystemHelper.Exists(source))
        {
            throw new IOException("O item não existe mais.");
        }

        if (FileSystemHelper.Exists(destination))
        {
            throw new IOException($"Já existe algo em {destination}.");
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var mv = CommandRunner.Find("mv") ?? "/bin/mv";
        var result = CommandRunner.Run(mv, ["--", source, destination], timeoutMs: 600_000);
        if (!result.Success)
        {
            var message = (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim();
            Log.Warn($"mv falhou ({result.ExitCode}): {source} -> {destination}: {message}");
            throw new IOException(message.Length > 0 ? message : $"mv terminou com o código {result.ExitCode}.");
        }
    }
}
