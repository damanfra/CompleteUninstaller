using System.Text;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Native;
using static CompleteUninstaller.Infrastructure.Inventory.ValueParsers;

namespace CompleteUninstaller.Infrastructure.Inventory;

/// <summary>
/// Enumera produtos pela API do Windows Installer (msi.dll). Encontra MSIs que não têm chave
/// Uninstall visível — inclusive os instalados por outros usuários.
/// </summary>
internal sealed class MsiSource
{
    public List<InstalledApp> Enumerate(List<string> errors)
    {
        var result = new List<InstalledApp>();
        try
        {
            string? userFilter = NativeMethods.SidEveryone;
            for (uint index = 0; ; index++)
            {
                var code = new StringBuilder(40);
                var sid = new StringBuilder(256);
                uint sidLength = (uint)sid.Capacity;

                var rc = NativeMethods.MsiEnumProductsEx(
                    null, userFilter, NativeMethods.MSIINSTALLCONTEXT_ALL, index, code, out var context, sid, ref sidLength);

                if (rc == NativeMethods.ERROR_ACCESS_DENIED && userFilter is not null && index == 0)
                {
                    // Sem privilégio para ver todos os usuários: cai para o usuário atual + máquina.
                    userFilter = null;
                    index = uint.MaxValue; // vira 0 no próximo incremento
                    continue;
                }

                if (rc == NativeMethods.ERROR_MORE_DATA)
                {
                    sid = new StringBuilder((int)sidLength + 1);
                    sidLength = (uint)sid.Capacity;
                    rc = NativeMethods.MsiEnumProductsEx(
                        null, userFilter, NativeMethods.MSIINSTALLCONTEXT_ALL, index, code, out context, sid, ref sidLength);
                }

                if (rc == NativeMethods.ERROR_NO_MORE_ITEMS)
                {
                    break;
                }

                if (rc != NativeMethods.ERROR_SUCCESS)
                {
                    errors.Add($"Windows Installer: erro {rc} ao enumerar produtos.");
                    break;
                }

                var productCode = code.ToString().ToUpperInvariant();
                var userSid = context == NativeMethods.MSIINSTALLCONTEXT_MACHINE ? null : sid.ToString();
                var name = GetInfo(productCode, userSid, context, "ProductName");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new InstalledApp
                {
                    Id = $"msi|{productCode}|{userSid}",
                    DisplayName = name.Trim(),
                    Source = AppSource.WindowsInstaller,
                    DisplayVersion = GetInfo(productCode, userSid, context, "VersionString"),
                    Publisher = GetInfo(productCode, userSid, context, "Publisher"),
                    InstallLocation = CleanPath(GetInfo(productCode, userSid, context, "InstallLocation")),
                    InstallDate = ParseDate(GetInfo(productCode, userSid, context, "InstallDate")),
                    MsiProductCode = productCode,
                    IsWindowsInstaller = true,
                    UserSid = userSid,
                });
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            errors.Add($"Windows Installer indisponível: {ex.Message}");
        }

        Log.Info($"Windows Installer: {result.Count} produtos enumerados.");
        return result;
    }

    private static string? GetInfo(string productCode, string? userSid, uint context, string property)
    {
        uint length = 512;
        var buffer = new StringBuilder((int)length);
        var rc = NativeMethods.MsiGetProductInfoEx(productCode, userSid, context, property, buffer, ref length);
        if (rc == NativeMethods.ERROR_MORE_DATA)
        {
            length++;
            buffer = new StringBuilder((int)length);
            rc = NativeMethods.MsiGetProductInfoEx(productCode, userSid, context, property, buffer, ref length);
        }

        if (rc != NativeMethods.ERROR_SUCCESS)
        {
            return null;
        }

        var value = buffer.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
