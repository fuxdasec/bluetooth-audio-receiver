using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BluetoothAudioReceiver.Core;

/// <summary>
/// The subset of semantic versioning this project produces: a three part core, an optional prerelease
/// label, and build metadata that carries no ordering.
/// </summary>
public sealed record AppVersion : IComparable<AppVersion>
{
    private AppVersion(int major, int minor, int patch, string? preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>The label after the first hyphen, for example <c>continuous.7</c>.</summary>
    public string? PreRelease { get; }

    /// <summary>
    /// Accepts the shapes the build produces: <c>v1.0.0</c>, <c>1.0.0</c>, <c>1.0.0+abc123</c> and
    /// <c>1.0.0-continuous.7+abc123</c>. Build metadata is discarded because it does not order.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        var metadata = candidate.IndexOf('+');
        if (metadata >= 0)
        {
            candidate = candidate[..metadata];
        }

        string? preRelease = null;
        var hyphen = candidate.IndexOf('-');
        if (hyphen >= 0)
        {
            preRelease = candidate[(hyphen + 1)..];
            candidate = candidate[..hyphen];
            if (preRelease.Length == 0 || !IsValidPreRelease(preRelease))
            {
                return false;
            }
        }

        var parts = candidate.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParseNumber(parts[0], out var major) ||
            !TryParseNumber(parts[1], out var minor) ||
            !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch, preRelease);
        return true;
    }

    private static bool TryParseNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// SemVer forbids leading zeros on numeric identifiers: "01" would compare equal to "1" while
    /// remaining a different record, which breaks ordering.
    /// </summary>
    private static bool IsValidPreRelease(string preRelease)
    {
        foreach (var identifier in preRelease.Split('.'))
        {
            if (identifier.Length > 1 &&
                identifier[0] == '0' &&
                identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major.CompareTo(other.Major);
        if (core != 0)
        {
            return core;
        }

        core = Minor.CompareTo(other.Minor);
        if (core != 0)
        {
            return core;
        }

        core = Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public bool IsNewerThan(AppVersion? other) => CompareTo(other) > 0;

    /// <summary>
    /// Semantic versioning ordering: a version carrying a prerelease label ranks below the same core
    /// without one, numeric identifiers compare numerically and rank below alphanumeric ones, and a
    /// shorter list of identifiers ranks below a longer one when every shared identifier is equal.
    /// </summary>
    private static int ComparePreRelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var shared = Math.Min(leftParts.Length, rightParts.Length);
        for (var index = 0; index < shared; index++)
        {
            var leftIsNumber = TryParseNumber(leftParts[index], out var leftNumber);
            var rightIsNumber = TryParseNumber(rightParts[index], out var rightNumber);
            if (leftIsNumber && rightIsNumber)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                {
                    return numeric;
                }

                continue;
            }

            if (leftIsNumber != rightIsNumber)
            {
                return leftIsNumber ? -1 : 1;
            }

            var text = string.CompareOrdinal(leftParts[index], rightParts[index]);
            if (text != 0)
            {
                return text < 0 ? -1 : 1;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    public override string ToString()
    {
        var core = string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}");
        return PreRelease is null ? core : $"{core}-{PreRelease}";
    }
}
