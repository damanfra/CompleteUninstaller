using Microsoft.Win32;

namespace CompleteUninstaller.Infrastructure.Platform;

/// <summary>Perfil de um usuário da máquina e as pastas relevantes dentro dele.</summary>
public sealed record UserProfileInfo(string Sid, string ProfilePath)
{
    public string UserName => Path.GetFileName(ProfilePath);

    public string AppData => Path.Combine(ProfilePath, "AppData");

    public string RoamingAppData => Path.Combine(AppData, "Roaming");

    public string LocalAppData => Path.Combine(AppData, "Local");

    public string LocalLowAppData => Path.Combine(AppData, "LocalLow");

    /// <summary>Raiz das instalações "só para mim" (VS Code, Discord, etc.).</summary>
    public string LocalPrograms => Path.Combine(LocalAppData, "Programs");

    public string Packages => Path.Combine(LocalAppData, "Packages");

    public string Temp => Path.Combine(LocalAppData, "Temp");

    public string StartMenu => Path.Combine(RoamingAppData, "Microsoft", "Windows", "Start Menu");

    public string StartMenuPrograms => Path.Combine(StartMenu, "Programs");

    public string Desktop => Path.Combine(ProfilePath, "Desktop");

    public IEnumerable<string> PersonalFolders =>
    [
        Path.Combine(ProfilePath, "Documents"),
        Path.Combine(ProfilePath, "Pictures"),
        Path.Combine(ProfilePath, "Music"),
        Path.Combine(ProfilePath, "Videos"),
        Path.Combine(ProfilePath, "Downloads"),
        Path.Combine(ProfilePath, "OneDrive"),
        Path.Combine(ProfilePath, "Favorites"),
        Path.Combine(ProfilePath, "Contacts"),
        Path.Combine(ProfilePath, "Saved Games"),
    ];
}

/// <summary>Pastas do sistema e perfis de usuário detectados nesta máquina.</summary>
public sealed class SystemPaths
{
    private SystemPaths()
    {
    }

    public required string WindowsDir { get; init; }

    public required string SystemDrive { get; init; }

    public required string ProgramFiles { get; init; }

    public required string ProgramFilesX86 { get; init; }

    public required string CommonProgramFiles { get; init; }

    public required string CommonProgramFilesX86 { get; init; }

    public required string ProgramData { get; init; }

    public required string CommonStartMenuPrograms { get; init; }

    public required string CommonDesktop { get; init; }

    public required string CurrentUserDesktop { get; init; }

    public required string UsersRoot { get; init; }

    public required string PublicProfile { get; init; }

    public required IReadOnlyList<UserProfileInfo> Profiles { get; init; }

    public string TasksFolder => Path.Combine(WindowsDir, "System32", "Tasks");

    public string PackageCache => Path.Combine(ProgramData, "Package Cache");

    public IEnumerable<string> ProgramRoots =>
        new[] { ProgramFiles, ProgramFilesX86 }
            .Concat(Profiles.Select(p => p.LocalPrograms))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public static SystemPaths Detect()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var currentProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var usersRoot = Path.GetDirectoryName(currentProfile) ?? @"C:\Users";
        var publicProfile = Path.Combine(usersRoot, "Public");
        var profiles = new List<UserProfileInfo>();

        try
        {
            using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (list is not null)
            {
                if (list.GetValue("ProfilesDirectory") is string dir && !string.IsNullOrWhiteSpace(dir))
                {
                    usersRoot = Environment.ExpandEnvironmentVariables(dir);
                }

                if (list.GetValue("Public") is string pub && !string.IsNullOrWhiteSpace(pub))
                {
                    publicProfile = Environment.ExpandEnvironmentVariables(pub);
                }

                foreach (var sid in list.GetSubKeyNames())
                {
                    // S-1-5-21-* = contas de usuário reais (locais ou de domínio).
                    if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var key = list.OpenSubKey(sid);
                    if (key?.GetValue("ProfileImagePath") is string path)
                    {
                        path = Environment.ExpandEnvironmentVariables(path);
                        if (Directory.Exists(path))
                        {
                            profiles.Add(new UserProfileInfo(sid, path));
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Logging.Log.Warn($"Não foi possível ler a lista de perfis: {ex.Message}");
        }

        if (!profiles.Any(p => string.Equals(p.ProfilePath, currentProfile, StringComparison.OrdinalIgnoreCase)))
        {
            profiles.Add(new UserProfileInfo(string.Empty, currentProfile));
        }

        return new SystemPaths
        {
            WindowsDir = windows,
            SystemDrive = Path.GetPathRoot(windows) ?? @"C:\",
            ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            CommonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            CommonProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            CommonStartMenuPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            CommonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            CurrentUserDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            UsersRoot = usersRoot,
            PublicProfile = publicProfile,
            Profiles = profiles,
        };
    }
}
