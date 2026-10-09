using System.Globalization;

namespace CompleteUninstaller.Core.Text;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long? bytes)
    {
        if (bytes is null || bytes.Value < 0)
        {
            return string.Empty;
        }

        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var culture = CultureInfo.CurrentCulture;
        return unit == 0
            ? string.Format(culture, "{0:0} {1}", value, Units[unit])
            : string.Format(culture, "{0:0.#} {1}", value, Units[unit]);
    }
}
