using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Infrastructure.Platform;

namespace CompleteUninstaller.Infrastructure.Leftovers;

/// <summary>Monta o <see cref="PathGuard"/> com as pastas protegidas desta máquina.</summary>
internal static class SafetyRules
{
    private static readonly string[] ProtectedProgramFilesChildren =
    [
        "Common Files", "Internet Explorer", "Microsoft Update Health Tools", "ModifiableWindowsApps",
        "Reference Assemblies", "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows Mail",
        "Windows Media Player", "Windows Multimedia Platform", "Windows NT", "Windows Photo Viewer",
        "Windows Portable Devices", "Windows Security", "Windows Sidebar", "WindowsApps", "WindowsPowerShell",
        "dotnet", "MSBuild", "Microsoft.NET", "PowerShell",
    ];

    private static readonly string[] ProtectedCommonFilesChildren =
    [
        "microsoft shared", "System", "Services", "SpeechEngines", "Designer",
    ];

    private static readonly string[] ProtectedStartMenuChildren =
    [
        "Administrative Tools", "Accessibility", "Accessories", "System Tools", "Windows PowerShell", "Windows Tools",
        "Maintenance", "Windows Administrative Tools", "Windows Accessories", "Windows Ease of Access", "Windows System",
    ];

    public static PathGuard CreateGuard(SystemPaths paths, IEnumerable<string> otherAppLocations)
    {
        var exact = new List<string>
        {
            paths.ProgramFiles,
            paths.ProgramFilesX86,
            paths.CommonProgramFiles,
            paths.CommonProgramFilesX86,
            paths.ProgramData,
            paths.PackageCache,
            Path.Combine(paths.ProgramData, "Packages"),
            paths.UsersRoot,
            paths.PublicProfile,
            paths.CommonStartMenuPrograms,
            Path.Combine(paths.CommonStartMenuPrograms, "Startup"),
            paths.CommonDesktop,
            paths.CurrentUserDesktop,
        };

        var trees = new List<string>
        {
            paths.WindowsDir,
            Path.Combine(paths.ProgramData, "Microsoft"),
            Path.Combine(paths.UsersRoot, "Default"),
            Path.Combine(paths.SystemDrive, "$Recycle.Bin"),
            Path.Combine(paths.SystemDrive, "System Volume Information"),
            Path.Combine(paths.SystemDrive, "Recovery"),
            Path.Combine(paths.SystemDrive, "Boot"),
            AppPaths.DataRoot,
            AppContext.BaseDirectory,
        };

        var allowed = new List<string> { paths.CommonStartMenuPrograms };

        foreach (var root in new[] { paths.ProgramFiles, paths.ProgramFilesX86 })
        {
            trees.AddRange(ProtectedProgramFilesChildren
                .Where(c => c != "Common Files")
                .Select(c => Path.Combine(root, c)));
        }

        foreach (var root in new[] { paths.CommonProgramFiles, paths.CommonProgramFilesX86 })
        {
            trees.AddRange(ProtectedCommonFilesChildren.Select(c => Path.Combine(root, c)));
        }

        trees.AddRange(ProtectedStartMenuChildren.Select(c => Path.Combine(paths.CommonStartMenuPrograms, c)));

        foreach (var profile in paths.Profiles)
        {
            exact.AddRange(
            [
                profile.ProfilePath,
                profile.AppData,
                profile.RoamingAppData,
                profile.LocalAppData,
                profile.LocalLowAppData,
                profile.LocalPrograms,
                profile.Packages,
                profile.Desktop,
                profile.StartMenu,
                profile.StartMenuPrograms,
                Path.Combine(profile.StartMenuPrograms, "Startup"),
            ]);

            trees.AddRange(
            [
                Path.Combine(profile.RoamingAppData, "Microsoft"),
                Path.Combine(profile.LocalAppData, "Microsoft"),
                Path.Combine(profile.LocalLowAppData, "Microsoft"),
                profile.Temp,
            ]);
            trees.AddRange(profile.PersonalFolders);
            trees.AddRange(ProtectedStartMenuChildren.Select(c => Path.Combine(profile.StartMenuPrograms, c)));

            allowed.Add(profile.StartMenuPrograms);
        }

        return new PathGuard(exact, trees, allowed, otherAppLocations);
    }
}
