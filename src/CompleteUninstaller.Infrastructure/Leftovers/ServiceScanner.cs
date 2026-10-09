using CompleteUninstaller.Core.Commands;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Matching;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Infrastructure.Platform;
using Microsoft.Win32;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>
/// Serviços Win32 deixados para trás. Drivers de kernel ficam de fora nesta fase (nível Avançado).
/// </summary>
internal static class ServiceScanner
{
    private const int ServiceWin32OwnProcess = 0x10;
    private const int ServiceWin32ShareProcess = 0x20;

    public static void Scan(ScanContext context, LeftoverCollector collector)
    {
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null)
        {
            return;
        }

        foreach (var name in services.GetSubKeyNames())
        {
            try
            {
                using var key = services.OpenSubKey(name);
                if (key is null)
                {
                    continue;
                }

                var type = key.GetValue("Type") is int t ? t : 0;
                if ((type & (ServiceWin32OwnProcess | ServiceWin32ShareProcess)) == 0)
                {
                    continue;
                }

                if (key.GetValue("ImagePath") is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
                {
                    continue;
                }

                var executable = ResolveBinary(imagePath, context.Paths.WindowsDir);
                if (executable is null || WinPath.IsSameOrUnder(executable, context.Paths.WindowsDir))
                {
                    continue; // serviços do próprio Windows (svchost etc.)
                }

                var displayName = key.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith('@'))
                {
                    displayName = name;
                }

                var details = $"{displayName} — {executable}";

                if (context.FindInstallDir(executable) is { } dir)
                {
                    Add(collector, name, details, dir.Confidence, "Serviço cujo executável fica na pasta do programa");
                    continue;
                }

                var match = Best(context.Matcher.Match(name), context.Matcher.Match(displayName));
                if (match == NameMatch.None || context.BelongsToAnotherApp(name) || context.BelongsToAnotherApp(displayName))
                {
                    continue;
                }

                var missing = !File.Exists(executable);
                if (missing)
                {
                    Add(collector, name, details, match == NameMatch.Exact ? Confidence.High : Confidence.Medium,
                        "Serviço com o nome do programa e executável inexistente");
                }
                else if (match == NameMatch.Exact)
                {
                    Add(collector, name, details, Confidence.Medium, "Serviço com o nome do programa");
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Chave sem permissão de leitura: ignora.
            }
        }
    }

    internal static string? ResolveBinary(string imagePath, string windowsDir)
    {
        var path = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(windowsDir, path[@"\SystemRoot\".Length..]);
        }

        var parsed = CommandLineParser.Parse(path, File.Exists);
        if (parsed is null)
        {
            return null;
        }

        var file = parsed.FileName;
        if (!WinPath.IsRootedLocal(file))
        {
            file = Path.Combine(windowsDir, "System32", file);
        }

        return WinPath.Normalize(file);
    }

    private static NameMatch Best(NameMatch a, NameMatch b)
    {
        if (a == NameMatch.Exact || b == NameMatch.Exact)
        {
            return NameMatch.Exact;
        }

        return a == NameMatch.Partial || b == NameMatch.Partial ? NameMatch.Partial : NameMatch.None;
    }

    private static void Add(LeftoverCollector collector, string serviceName, string details, Confidence confidence, string reason) =>
        collector.Add(new LeftoverItem
        {
            Kind = LeftoverKind.Service,
            Target = serviceName,
            Details = details,
            Confidence = confidence,
            Reason = reason,
        });
}
