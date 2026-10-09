using System.Xml.Linq;
using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>
/// Tarefas agendadas que executam arquivos do programa. Lê as definições XML em C:\Windows\System32\Tasks
/// (somente leitura); a remoção é feita depois pelo schtasks.exe.
/// </summary>
internal static class ScheduledTaskScanner
{
    public static void Scan(ScanContext context, LeftoverCollector collector)
    {
        var root = context.Paths.TasksFolder;
        if (!Directory.Exists(root))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            }).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            var taskPath = file[root.Length..];
            if (!taskPath.StartsWith('\\'))
            {
                taskPath = "\\" + taskPath;
            }

            if (taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
            {
                continue; // tarefas do Windows
            }

            var commands = ReadCommands(file, context.Paths.WindowsDir);
            if (commands.Count == 0)
            {
                continue;
            }

            var details = string.Join(" | ", commands);

            var installDir = commands.Select(context.FindInstallDir).FirstOrDefault(d => d is not null);
            if (installDir is not null)
            {
                Add(collector, taskPath, details, installDir.Confidence, "Tarefa agendada executa um arquivo da pasta do programa");
                continue;
            }

            if (commands.All(c => WinPath.IsSameOrUnder(c, context.Paths.WindowsDir)))
            {
                continue; // só chama executáveis do Windows: não dá para afirmar nada pelo destino
            }

            var name = WinPath.GetLeaf(taskPath);
            var folder = WinPath.GetParent(taskPath) is { Length: > 1 } parent ? WinPath.GetLeaf(parent) : null;
            var match = context.Matcher.Match(name);
            if (match == NameMatch.None && folder is not null && context.Matcher.Match(folder) == NameMatch.Exact)
            {
                match = NameMatch.Partial; // pasta da tarefa com o nome do programa
            }

            if (match is not (NameMatch.Exact or NameMatch.Partial) || context.BelongsToAnotherApp(name))
            {
                continue;
            }

            var allMissing = commands.All(c => !File.Exists(c));
            if (allMissing)
            {
                Add(collector, taskPath, details, match == NameMatch.Exact ? Confidence.High : Confidence.Medium,
                    "Tarefa agendada com o nome do programa apontando para arquivo inexistente");
            }
            else if (match == NameMatch.Exact)
            {
                Add(collector, taskPath, details, Confidence.Medium, "Tarefa agendada com o nome do programa");
            }
        }
    }

    private static List<string> ReadCommands(string taskFile, string windowsDir)
    {
        try
        {
            var document = XDocument.Load(taskFile);
            return document.Descendants()
                .Where(e => e.Name.LocalName == "Command")
                .Select(e => e.Value.Trim())
                .Where(v => v.Length > 0)
                .Select(v => ResolveCommand(v, windowsDir))
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ResolveCommand(string command, string windowsDir)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return null;
        }

        if (WinPath.IsRootedLocal(expanded))
        {
            return WinPath.Normalize(expanded);
        }

        // Comando sem caminho (ex.: "cmd.exe"): resolve para System32.
        var parsed = CommandLineParser.Parse(expanded, File.Exists);
        return parsed is null ? null : WinPath.Normalize(Path.Combine(windowsDir, "System32", parsed.FileName));
    }

    private static void Add(LeftoverCollector collector, string taskPath, string details, Confidence confidence, string reason) =>
        collector.Add(new LeftoverItem
        {
            Kind = LeftoverKind.ScheduledTask,
            Target = taskPath,
            Details = details,
            Confidence = confidence,
            Reason = reason,
        });
}
