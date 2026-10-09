using System.Globalization;

namespace CompleteUninstaller.Core.Updates;

/// <summary>Versão no estilo semântico: 1.2.3, v1.2.3, 1.2.3-beta.1, 1.2.3+commit. A pré-versão vale menos que a final.</summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly int[] _numbers;

    private AppVersion(int[] numbers, string? prerelease)
    {
        _numbers = numbers;
        Prerelease = prerelease;
    }

    public string? Prerelease { get; }

    public static AppVersion Zero { get; } = new([0, 0, 0], null);

    public static bool TryParse(string? text, out AppVersion version)
    {
        version = Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            s = s[..plus]; // metadados de build não contam
        }

        string? prerelease = null;
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = s[(dash + 1)..];
            s = s[..dash];
            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        var parts = s.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[Math.Max(3, parts.Length)];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new AppVersion(numbers, prerelease);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (var i = 0; i < Math.Max(_numbers.Length, other._numbers.Length); i++)
        {
            var a = i < _numbers.Length ? _numbers[i] : 0;
            var b = i < other._numbers.Length ? other._numbers[i] : 0;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_numbers.Take(3).Aggregate(17, HashCode.Combine), Prerelease);

    public override string ToString() =>
        string.Join('.', _numbers.Take(Math.Max(3, _numbers.Length))) + (Prerelease is null ? string.Empty : "-" + Prerelease);

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;

    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    /// <summary>Sem pré-versão &gt; com pré-versão; identificadores numéricos comparam como número e valem menos que texto.</summary>
    private static int ComparePrerelease(string? a, string? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }

        if (a is null)
        {
            return 1;
        }

        if (b is null)
        {
            return -1;
        }

        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var aNumeric = int.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var an);
            var bNumeric = int.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
            int result;
            if (aNumeric && bNumeric)
            {
                result = an.CompareTo(bn);
            }
            else if (aNumeric)
            {
                result = -1;
            }
            else if (bNumeric)
            {
                result = 1;
            }
            else
            {
                result = string.CompareOrdinal(left[i], right[i]);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}
