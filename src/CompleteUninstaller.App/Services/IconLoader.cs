using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CompleteUninstaller.Infrastructure.Inventory;

namespace CompleteUninstaller.App.Services;

/// <summary>
/// Converte um <see cref="IconSource"/> em imagem WPF congelada (pode ser criada fora da thread da interface).
/// Usa cache por arquivo+índice e cai no ícone padrão de aplicativo do Windows quando não há ícone.
/// </summary>
internal static class IconLoader
{
    private const int IconSize = 32;
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<ImageSource?> DefaultIconLazy = new(LoadDefaultIcon);

    public static ImageSource? DefaultIcon => DefaultIconLazy.Value;

    public static ImageSource? Load(IconSource? source)
    {
        if (source is null)
        {
            return DefaultIcon;
        }

        var image = Cache.GetOrAdd($"{source.Path}|{source.Index}", _ => LoadCore(source));
        return image ?? DefaultIcon;
    }

    private static ImageSource? LoadCore(IconSource source)
    {
        try
        {
            return source.IsImageFile ? LoadImageFile(source.Path) : LoadIconResource(source.Path, source.Index);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static ImageSource LoadImageFile(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = IconSize;
        image.CacheOption = BitmapCacheOption.OnLoad; // lê tudo agora e libera o arquivo
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static ImageSource? LoadIconResource(string path, int index)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        var count = ExtractIconEx(path, index, large, small, 1);
        try
        {
            if (count == 0 || count == uint.MaxValue)
            {
                return null;
            }

            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            return handle == IntPtr.Zero ? null : FromHIcon(handle);
        }
        finally
        {
            if (large[0] != IntPtr.Zero)
            {
                DestroyIcon(large[0]);
            }

            if (small[0] != IntPtr.Zero)
            {
                DestroyIcon(small[0]);
            }
        }
    }

    private static ImageSource? LoadDefaultIcon()
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(SIID_APPLICATION, SHGSI_ICON | SHGSI_LARGEICON, ref info) != 0 || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return FromHIcon(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static ImageSource FromHIcon(IntPtr handle)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    // ------------------------------------------------------------------ Win32
    private const uint SIID_APPLICATION = 2;
    private const uint SHGSI_ICON = 0x000000100;
    private const uint SHGSI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

    [DllImport("shell32.dll")]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
