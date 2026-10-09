using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.Core.Models;

/// <summary>Classifica programas para os filtros "Ocultar itens do Windows" e "Ocultar apps da Microsoft".</summary>
public static class AppClassifier
{
    /// <summary>Programas da Microsoft que vêm com o Windows mas são registrados como apps comuns.</summary>
    private static readonly HashSet<string> BundledWithWindows = new(StringComparer.Ordinal)
    {
        "microsoftedge",
        "microsoftedgeupdate",
        "microsoftedgewebview2runtime",
        "microsoftonedrive",
        "microsoftupdatehealthtools",
        "microsoftgameinput",
        "microsoftstore",
    };

    public static bool IsMicrosoft(InstalledApp app)
    {
        var publisher = NameNormalizer.NormalizePublisher(app.Publisher);
        if (publisher == "microsoft" || publisher.StartsWith("microsoft ", StringComparison.Ordinal))
        {
            return true;
        }

        return app.Source == AppSource.Store
            && app.PackageFamilyName is { } family
            && (family.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                || family.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase)
                || family.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Faz parte do Windows: app da Store do sistema ou pré-instalado, ou programa da Microsoft que acompanha o Windows.</summary>
    public static bool IsWindowsComponent(InstalledApp app)
    {
        if (!IsMicrosoft(app))
        {
            return false;
        }

        if (app.Source == AppSource.Store
            && (app.Flags.HasFlag(AppFlags.Provisioned) || app.Flags.HasFlag(AppFlags.SystemComponent)))
        {
            return true;
        }

        var name = NameNormalizer.Normalize(app.DisplayName);
        if (name == "windows"
            || name.StartsWith("windows ", StringComparison.Ordinal)
            || name.StartsWith("microsoft windows ", StringComparison.Ordinal))
        {
            return true;
        }

        return BundledWithWindows.Contains(NameNormalizer.Compact(name));
    }
}
