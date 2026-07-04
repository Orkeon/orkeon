using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Semantic version value object (major.minor.patch[-prerelease]).
/// </summary>
/// <remarks>
/// Named <see cref="SemanticVersion"/> (not <c>Version</c>) to avoid masking
/// <see cref="System.Version"/> in consuming code.
/// </remarks>
public sealed record SemanticVersion : ValueObjectRecord, IComparable<SemanticVersion>
{
    /// <summary>Gets the major version number.</summary>
    public int Major { get; }
    /// <summary>Gets the minor version number.</summary>
    public int Minor { get; }
    /// <summary>Gets the patch version number.</summary>
    public int Patch { get; }
    /// <summary>Gets the optional pre-release label (e.g., "alpha", "beta").</summary>
    public string? PreRelease { get; }

    /// <summary>Initializes a new <see cref="SemanticVersion"/> with the specified components.</summary>
    /// <param name="major">The major version number (non-negative).</param>
    /// <param name="minor">The minor version number (non-negative).</param>
    /// <param name="patch">The patch version number (non-negative).</param>
    /// <param name="preRelease">The optional pre-release label.</param>
    private SemanticVersion(int major, int minor, int patch, string? preRelease = null)
    {
        Major = major >= 0 ? major : throw new ArgumentException("Major version cannot be negative", nameof(major));
        Minor = minor >= 0 ? minor : throw new ArgumentException("Minor version cannot be negative", nameof(minor));
        Patch = patch >= 0 ? patch : throw new ArgumentException("Patch version cannot be negative", nameof(patch));
        PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease.Trim();
    }

    /// <summary>Creates a new <see cref="SemanticVersion"/>.</summary>
    /// <param name="major">The major version number (non-negative).</param>
    /// <param name="minor">The minor version number (non-negative).</param>
    /// <param name="patch">The patch version number (non-negative).</param>
    /// <param name="preRelease">The optional pre-release label.</param>
    /// <returns>A new <see cref="SemanticVersion"/>.</returns>
    public static SemanticVersion Create(int major, int minor, int patch, string? preRelease = null) =>
        new(major, minor, patch, preRelease);

    /// <summary>Parses a version string in the format "major.minor.patch[-prerelease]".</summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>A parsed <see cref="SemanticVersion"/> instance.</returns>
    public static SemanticVersion Parse(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var parts = version.Split('-');
        var versionParts = parts[0].Split('.');

        if (versionParts.Length != 3)
            throw new ArgumentException("Version must have format major.minor.patch", nameof(version));

        if (!int.TryParse(versionParts[0], out var major) ||
            !int.TryParse(versionParts[1], out var minor) ||
            !int.TryParse(versionParts[2], out var patch))
            throw new ArgumentException("Invalid version format", nameof(version));

        var preRelease = parts.Length > 1 ? parts[1] : null;
        return Create(major, minor, patch, preRelease);
    }

    /// <summary>Gets whether this version is a pre-release.</summary>
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);
    /// <summary>Gets whether this version is stable (not a pre-release).</summary>
    public bool IsStable => !IsPreRelease;

    /// <summary>Returns a new version with the major component incremented and minor/patch reset to 0.</summary>
    /// <returns>A new <see cref="SemanticVersion"/> with incremented major.</returns>
    public SemanticVersion IncrementMajor() => Create(Major + 1, 0, 0);
    /// <summary>Returns a new version with the minor component incremented and patch reset to 0.</summary>
    /// <returns>A new <see cref="SemanticVersion"/> with incremented minor.</returns>
    public SemanticVersion IncrementMinor() => Create(Major, Minor + 1, 0);
    /// <summary>Returns a new version with the patch component incremented.</summary>
    /// <returns>A new <see cref="SemanticVersion"/> with incremented patch.</returns>
    public SemanticVersion IncrementPatch() => Create(Major, Minor, Patch + 1);

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0) return majorComparison;

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0) return minorComparison;

        var patchComparison = Patch.CompareTo(other.Patch);
        if (patchComparison != 0) return patchComparison;

        // Pre-release versions have lower precedence
        return (IsPreRelease, other.IsPreRelease) switch
        {
            (true, false) => -1,
            (false, true) => 1,
            (true, true) => string.Compare(PreRelease, other.PreRelease, StringComparison.Ordinal),
            _ => 0
        };
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator >(SemanticVersion left, SemanticVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) > 0;
    }
    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator <(SemanticVersion left, SemanticVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) < 0;
    }
    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator >=(SemanticVersion left, SemanticVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) >= 0;
    }
    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    public static bool operator <=(SemanticVersion left, SemanticVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) <= 0;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        return IsPreRelease ? $"{version}-{PreRelease}" : version;
    }
}
