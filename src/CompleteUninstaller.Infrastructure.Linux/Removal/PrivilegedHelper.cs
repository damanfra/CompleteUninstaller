using System.Text.Json;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Linux;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Native;
using CompleteUninstaller.Infrastructure.Linux.Platform;

namespace CompleteUninstaller.Infrastructure.Linux.Removal;

/// <summary>
/// A única parte do aplicativo que roda como root (iniciada com <c>pkexec &lt;app&gt; --helper</c>).
/// Move itens do sistema (/opt, /etc, /var/lib...) para a quarentena em /var/lib/CompleteUninstaller e de volta.
/// NÃO confia no pedido: valida de novo cada caminho com a proteção de caminhos e a posse por pacote
/// instalado, aceita só operações conhecidas e só mexe dentro da quarentena e das áreas permitidas.
/// </summary>
public static class PrivilegedHelper
{
    /// <summary>Ponto de entrada de <c>--helper</c>: lê o pedido em JSON e escreve a resposta em JSON.</summary>
    public static int Run(TextReader input, TextWriter output)
    {
        HelperResponse response;
        try
        {
            var buffer = new char[HelperProtocol.MaxRequestBytes + 1];
            var read = input.ReadBlock(buffer, 0, buffer.Length);
            if (read > HelperProtocol.MaxRequestBytes)
            {
                response = Fail("Pedido grande demais.");
            }
            else
            {
                var request = JsonSerializer.Deserialize<HelperRequest>(new string(buffer, 0, read), HelperProtocol.Json);
                response = request is null ? Fail("Pedido vazio.") : Execute(request);
            }
        }
        catch (JsonException ex)
        {
            response = Fail($"Pedido ilegível: {ex.Message}");
        }

        output.Write(HelperProtocol.Serialize(response));
        output.Flush();
        return response.Ok ? 0 : 1;
    }

    public static HelperResponse Execute(HelperRequest request)
    {
        if (!Libc.IsRoot())
        {
            return Fail("O auxiliar precisa rodar como root.");
        }

        if (!HelperProtocol.IsValidSessionId(request.SessionId))
        {
            return Fail("Identificador de sessão inválido.");
        }

        if (request.Items.Count > HelperProtocol.MaxItems)
        {
            return Fail("Itens demais no pedido.");
        }

        try
        {
            var sessionDir = Path.Combine(AppPaths.SystemQuarantineRoot, request.SessionId);
            switch (request.Op)
            {
                case "move":
                    PrepareQuarantineRoot();
                    return Move(request, sessionDir);

                case "restore":
                    return Restore(request, sessionDir);

                case "purge":
                    if (Directory.Exists(sessionDir))
                    {
                        Directory.Delete(sessionDir, recursive: true);
                        Log.Info($"[auxiliar] Quarentena do sistema excluída: {sessionDir}");
                    }

                    return new HelperResponse { Ok = true };

                default:
                    return Fail("Operação desconhecida.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("[auxiliar] Falha", ex);
            return Fail(ex.Message);
        }
    }

    private static HelperResponse Move(HelperRequest request, string sessionDir)
    {
        var guard = CreateGuard();
        var response = new HelperResponse { Ok = true };
        var reloadSystemd = false;

        // Serviços primeiro (liberam os executáveis), depois o resto.
        foreach (var item in request.Items.OrderBy(i => i.Kind == "Service" ? 0 : 1))
        {
            var result = new HelperResult { Id = item.Id };
            response.Results.Add(result);

            if (!ValidateItem(item, guard, out var error))
            {
                result.Error = error;
                result.Status = "Skipped";
                Log.Warn($"[auxiliar] Recusado: {item.Path} — {error}");
                continue;
            }

            try
            {
                if (!FileSystemHelper.Exists(item.Path))
                {
                    result.Status = "Skipped";
                    result.Error = "O item não existe mais.";
                    continue;
                }

                if (FileSystemHelper.IsLink(item.Path))
                {
                    result.Status = "Skipped";
                    result.Error = "Links simbólicos não são movidos.";
                    continue;
                }

                if (item.Kind == "Service")
                {
                    var unit = UnixPath.GetLeaf(item.Path);
                    CommandRunner.Run("systemctl", ["disable", "--now", unit], timeoutMs: 60_000);
                    reloadSystemd = true;
                }

                var stored = $"items/{item.Id}/{UnixPath.GetLeaf(item.Path)}";
                FileMover.Move(item.Path, Path.Combine(sessionDir, stored));
                result.Ok = true;
                result.Status = item.Kind == "Service" ? "Removed" : "Quarantined";
                result.Stored = stored;
                Log.Info($"[auxiliar] Movido para a quarentena do sistema: {item.Path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Status = "Failed";
                result.Error = ex.Message;
                Log.Error($"[auxiliar] Falha movendo {item.Path}", ex);
            }
        }

        if (reloadSystemd)
        {
            CommandRunner.Run("systemctl", ["daemon-reload"], timeoutMs: 60_000);
        }

        return response;
    }

    private static HelperResponse Restore(HelperRequest request, string sessionDir)
    {
        var guard = CreateGuard();
        var response = new HelperResponse { Ok = true };
        var reloadSystemd = false;

        foreach (var item in request.Items)
        {
            var result = new HelperResult { Id = item.Id };
            response.Results.Add(result);

            if (!ValidateItem(item, guard, out var error))
            {
                result.Error = error;
                result.Status = "Failed";
                continue;
            }

            if (item.Stored is null || !HelperProtocol.IsValidStored(item.Stored, item.Id))
            {
                result.Error = "Caminho da quarentena inválido.";
                result.Status = "Failed";
                continue;
            }

            try
            {
                var stored = Path.Combine(sessionDir, item.Stored);
                if (!FileSystemHelper.Exists(stored))
                {
                    throw new IOException("A cópia em quarentena não foi encontrada.");
                }

                FileMover.Move(stored, item.Path);
                reloadSystemd |= item.Kind == "Service";
                result.Ok = true;
                result.Status = "Restored";
                Log.Info($"[auxiliar] Restaurado: {item.Path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Status = "Failed";
                result.Error = ex.Message;
            }
        }

        if (reloadSystemd)
        {
            CommandRunner.Run("systemctl", ["daemon-reload"], timeoutMs: 60_000);
        }

        return response;
    }

    /// <summary>Regras que valem para qualquer item, em qualquer operação.</summary>
    internal static bool ValidateItem(HelperItem item, PathGuard guard, out string error)
    {
        if (!HelperProtocol.IsValidItemId(item.Id))
        {
            error = "Identificador de item inválido.";
            return false;
        }

        // Só o nome exato do tipo ("Folder"); números como "1" também seriam aceitos por Enum.TryParse.
        if (!Enum.GetNames<LeftoverKind>().Contains(item.Kind, StringComparer.Ordinal)
            || !Enum.TryParse<LeftoverKind>(item.Kind, out var kind)
            || kind is LeftoverKind.ScheduledTask)
        {
            error = "Tipo de item inválido.";
            return false;
        }

        if (!UnixPath.IsRootedLocal(item.Path))
        {
            error = "Caminho inválido.";
            return false;
        }

        var path = UnixPath.Normalize(item.Path);
        if (!guard.CanRemove(path, out var why))
        {
            error = why;
            return false;
        }

        if (kind == LeftoverKind.Service
            && (!UnixPath.AreEqual(UnixPath.GetParent(path) ?? string.Empty, "/etc/systemd/system")
                || !HelperProtocol.IsValidUnitName(UnixPath.GetLeaf(path))))
        {
            error = "Unidade do systemd fora de /etc/systemd/system.";
            return false;
        }

        item.Path = path;
        error = string.Empty;
        return true;
    }

    /// <summary>A proteção do auxiliar: só áreas do sistema, nada da pasta pessoal, nada de pacote instalado.</summary>
    private static PathGuard CreateGuard()
    {
        var ownership = new PackageOwnership(InstalledPackageNames());

        // Sem perfis: nenhuma pasta pessoal é conhecida, e /home e /root ficam fora das áreas permitidas.
        return UnixSafetyRules.CreateGuard([], [AppPaths.SystemDataRoot], [], ownership.OwnerOf);
    }

    private static List<string> InstalledPackageNames()
    {
        var names = new List<string>();
        if (CommandRunner.Exists("dpkg-query"))
        {
            var result = CommandRunner.Run("dpkg-query", ["-W", $"-f={PackageParsers.DpkgQueryFormat}"], timeoutMs: 120_000);
            names.AddRange(PackageParsers.ParseDpkg(result.Output).Installed.Select(a => a.PackageName!));
        }

        if (CommandRunner.Exists("rpm"))
        {
            var result = CommandRunner.Run("rpm", ["-qa", "--qf", "%{NAME}\\n"], timeoutMs: 120_000);
            names.AddRange(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return names;
    }

    private static void PrepareQuarantineRoot()
    {
        Directory.CreateDirectory(AppPaths.SystemQuarantineRoot);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(AppPaths.SystemDataRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static HelperResponse Fail(string error) => new() { Ok = false, Error = error };
}
