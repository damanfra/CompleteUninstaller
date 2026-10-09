using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Core.Commands;

public enum UninstallMethod
{
    CommandLine,
    WindowsInstaller,
    StorePackage,
}

/// <param name="Method">Como a desinstalação será feita.</param>
/// <param name="CommandLine">Linha de comando completa (ou o PackageFullName, para a Store).</param>
/// <param name="WorkingDirectory">Pasta do executável, quando conhecida.</param>
/// <param name="IsQuiet">Se o modo silencioso foi escolhido.</param>
public sealed record UninstallPlan(UninstallMethod Method, string CommandLine, string? WorkingDirectory, bool IsQuiet);

/// <summary>Decide qual comando oficial usar para desinstalar um programa.</summary>
public static class UninstallCommandBuilder
{
    public static UninstallPlan? Build(InstalledApp app, bool preferQuiet, Func<string, bool> fileExists)
    {
        if (app.Source == AppSource.Store)
        {
            return string.IsNullOrWhiteSpace(app.PackageFullName)
                ? null
                : new UninstallPlan(UninstallMethod.StorePackage, app.PackageFullName, null, false);
        }

        var codeInCommand = CommandLineParser.ExtractMsiProductCode(app.UninstallString);
        var productCode = app.MsiProductCode ?? codeInCommand;
        if (productCode is not null
            && (app.IsWindowsInstaller || codeInCommand is not null || string.IsNullOrWhiteSpace(app.UninstallString)))
        {
            // "/I{...}" abre a tela de manutenção; "/x" remove de fato.
            var arguments = preferQuiet
                ? $"/x {productCode} /qb REBOOT=ReallySuppress"
                : $"/x {productCode}";
            return new UninstallPlan(UninstallMethod.WindowsInstaller, $"msiexec.exe {arguments}", null, preferQuiet);
        }

        var useQuiet = preferQuiet && !string.IsNullOrWhiteSpace(app.QuietUninstallString);
        var command = useQuiet ? app.QuietUninstallString : app.UninstallString;
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var parsed = CommandLineParser.Parse(command, fileExists);
        var workingDirectory = parsed is not null && WinPath.IsRootedLocal(parsed.FileName)
            ? WinPath.GetParent(parsed.FileName)
            : null;
        return new UninstallPlan(UninstallMethod.CommandLine, command.Trim(), workingDirectory, useQuiet);
    }
}
