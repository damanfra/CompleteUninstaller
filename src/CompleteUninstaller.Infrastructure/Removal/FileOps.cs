using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Native;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Removal;

/// <summary>Mover para a quarentena, restaurar e apagar árvores de arquivos com segurança.</summary>
internal static class FileOps
{
    /// <summary>
    /// Move o item para <paramref name="destination"/>. No mesmo volume é um simples "renomear" (atômico);
    /// se falhar (arquivo em uso) ou se o volume for outro, copia e apaga o original, agendando para a
    /// reinicialização o que estiver bloqueado.
    /// </summary>
    public static void MoveToQuarantine(string source, string destination, QuarantineEntry entry)
    {
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
        {
            entry.Status = EntryStatus.Skipped;
            entry.Error = "O item não existe mais.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (SameVolume(source, destination))
        {
            try
            {
                if (isDirectory)
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    File.Move(source, destination);
                }

                entry.Status = EntryStatus.Quarantined;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Não foi possível mover de uma vez ({ex.Message}); copiando item a item: {source}");
            }
        }

        var copyFailures = isDirectory ? CopyDirectory(source, destination, overwrite: true) : CopyFile(source, destination, overwrite: true);
        entry.PartialCopy = copyFailures > 0;
        var pending = DeleteTree(source);
        entry.Status = pending ? EntryStatus.PendingReboot : EntryStatus.Quarantined;
    }

    /// <summary>Devolve um item da quarentena ao local original (sem sobrescrever o que já existir lá).</summary>
    public static void Restore(string stored, string original)
    {
        var isDirectory = Directory.Exists(stored);
        if (!isDirectory && !File.Exists(stored))
        {
            throw new IOException("A cópia em quarentena não foi encontrada.");
        }

        var parent = Path.GetDirectoryName(original);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (isDirectory)
        {
            if (!Directory.Exists(original) && SameVolume(stored, original))
            {
                Directory.Move(stored, original);
                return;
            }

            var failures = CopyDirectory(stored, original, overwrite: false);
            if (failures > 0)
            {
                throw new IOException($"{failures} arquivo(s) não puderam ser restaurados.");
            }

            DeleteTree(stored);
            return;
        }

        if (File.Exists(original))
        {
            throw new IOException("Já existe um arquivo no local original.");
        }

        if (SameVolume(stored, original))
        {
            File.Move(stored, original);
        }
        else
        {
            File.Copy(stored, original);
            File.Delete(stored);
        }
    }

    /// <summary>Apaga um arquivo ou pasta sem seguir junções. Retorna true se algo ficou agendado para a reinicialização.</summary>
    public static bool DeleteTree(string path)
    {
        if (File.Exists(path))
        {
            return !TryDeleteFile(path);
        }

        if (!Directory.Exists(path))
        {
            return false;
        }

        var pending = false;
        var directories = new List<string>();
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            directories.Add(dir);
            if (!string.Equals(dir, path, StringComparison.OrdinalIgnoreCase) && FileSystemHelper.IsReparsePoint(dir))
            {
                continue; // remove só o link, nunca o conteúdo do destino
            }

            foreach (var file in FileSystemHelper.GetFiles(dir))
            {
                pending |= !TryDeleteFile(file);
            }

            foreach (var sub in FileSystemHelper.GetDirectories(dir))
            {
                stack.Push(sub);
            }
        }

        foreach (var dir in directories.OrderByDescending(d => d.Length))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                if ((info.Attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                {
                    info.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden);
                }

                Directory.Delete(dir, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                pending = true;
                if (!NativeMethods.MoveFileEx(dir, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT))
                {
                    Log.Warn($"Pasta não removida nem agendada: {dir} ({ex.Message})");
                }
            }
        }

        return pending;
    }

    private static bool TryDeleteFile(string file)
    {
        try
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (NativeMethods.MoveFileEx(file, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT))
            {
                Log.Info($"Em uso; agendado para remoção na reinicialização: {file}");
            }
            else
            {
                Log.Warn($"Arquivo não removido nem agendado: {file} ({ex.Message})");
            }

            return false;
        }
    }

    private static int CopyFile(string source, string destination, bool overwrite)
    {
        try
        {
            File.Copy(source, destination, overwrite);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!overwrite && File.Exists(destination))
            {
                return 0; // já existe no destino: mantém o atual
            }

            Log.Warn($"Falha ao copiar {source}: {ex.Message}");
            return 1;
        }
    }

    private static int CopyDirectory(string source, string destination, bool overwrite)
    {
        var failures = 0;
        var stack = new Stack<(string Source, string Destination)>();
        stack.Push((source, destination));
        while (stack.Count > 0)
        {
            var (src, dst) = stack.Pop();
            Directory.CreateDirectory(dst);
            foreach (var file in FileSystemHelper.GetFiles(src))
            {
                failures += CopyFile(file, Path.Combine(dst, Path.GetFileName(file)), overwrite);
            }

            foreach (var sub in FileSystemHelper.GetDirectories(src))
            {
                if (FileSystemHelper.IsReparsePoint(sub))
                {
                    Log.Warn($"Link/junção não copiado: {sub}");
                    continue;
                }

                stack.Push((sub, Path.Combine(dst, Path.GetFileName(sub))));
            }
        }

        return failures;
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}
