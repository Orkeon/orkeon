using System.Globalization;

namespace Orkeon.Plugins;

/// <summary>
/// A semantic version (<c>major.minor[.patch]</c>, optional pre-release, build metadata ignored).
/// Orkeon ships release candidates (<c>1.0.0-rc.6</c>), so pre-release ordering is part of the contract.
/// </summary>
internal sealed class PluginSemVer : IComparable<PluginSemVer>
{
    private readonly int[] _numbers;
    private readonly string[] _pre;

    private PluginSemVer(int[] numbers, string[] pre)
    {
        _numbers = numbers;
        _pre = pre;
    }

    public static PluginSemVer? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var value = text.Trim();
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            value = value[..plus];

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? value : value[..dash];
        var pre = dash < 0 ? [] : value[(dash + 1)..].Split('.');
        if (pre.Any(static p => p.Length == 0))
            return null;

        var parts = core.Split('.');
        if (parts.Length is < 1 or > 4)
            return null;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return null;
        }

        return new PluginSemVer(numbers, pre);
    }

    public int CompareTo(PluginSemVer? other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (var i = 0; i < _numbers.Length; i++)
        {
            var byNumber = _numbers[i].CompareTo(other._numbers[i]);
            if (byNumber != 0)
                return byNumber;
        }

        // A version without pre-release sorts above the same version with one.
        if (_pre.Length == 0 || other._pre.Length == 0)
            return other._pre.Length.CompareTo(_pre.Length);

        for (var i = 0; i < Math.Min(_pre.Length, other._pre.Length); i++)
        {
            var byIdentifier = CompareIdentifier(_pre[i], other._pre[i]);
            if (byIdentifier != 0)
                return byIdentifier;
        }

        return _pre.Length.CompareTo(other._pre.Length);
    }

    public override bool Equals(object? obj) => obj is PluginSemVer other && CompareTo(other) == 0;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var number in _numbers)
            hash.Add(number);
        return hash.ToHashCode();
    }

    public static bool operator ==(PluginSemVer? left, PluginSemVer? right) => Compare(left, right) == 0;

    public static bool operator !=(PluginSemVer? left, PluginSemVer? right) => Compare(left, right) != 0;

    public static bool operator <(PluginSemVer? left, PluginSemVer? right) => Compare(left, right) < 0;

    public static bool operator <=(PluginSemVer? left, PluginSemVer? right) => Compare(left, right) <= 0;

    public static bool operator >(PluginSemVer? left, PluginSemVer? right) => Compare(left, right) > 0;

    public static bool operator >=(PluginSemVer? left, PluginSemVer? right) => Compare(left, right) >= 0;

    /// <summary>Orders two versions, a missing one sorting first.</summary>
    private static int Compare(PluginSemVer? left, PluginSemVer? right)
    {
        if (left is null)
            return right is null ? 0 : -1;
        return right is null ? 1 : left.CompareTo(right);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var l);
        var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var r);
        if (leftNumeric && rightNumeric)
            return l.CompareTo(r);
        if (leftNumeric)
            return -1;
        if (rightNumeric)
            return 1;
        return string.CompareOrdinal(left, right);
    }
}

/// <summary>
/// A NuGet-style version range: <c>1.0.0</c> (at least), <c>[1.0.0]</c> (exactly),
/// <c>[1.0.0,2.0.0)</c>, <c>(,2.0.0]</c> and the like, or <c>*</c> (any).
/// </summary>
internal sealed class PluginVersionRange
{
    private readonly PluginSemVer? _min;
    private readonly bool _minInclusive;
    private readonly PluginSemVer? _max;
    private readonly bool _maxInclusive;

    private PluginVersionRange(PluginSemVer? min, bool minInclusive, PluginSemVer? max, bool maxInclusive)
    {
        _min = min;
        _minInclusive = minInclusive;
        _max = max;
        _maxInclusive = maxInclusive;
    }

    public static PluginVersionRange? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var value = text.Trim();
        if (value == "*")
            return new PluginVersionRange(null, true, null, true);

        if (value[0] is not ('[' or '('))
            return PluginSemVer.TryParse(value) is { } floor ? new PluginVersionRange(floor, true, null, true) : null;

        if (value.Length < 3 || value[^1] is not (']' or ')'))
            return null;

        var minInclusive = value[0] == '[';
        var maxInclusive = value[^1] == ']';
        var parts = value[1..^1].Split(',');
        return parts.Length switch
        {
            1 when minInclusive && maxInclusive && PluginSemVer.TryParse(parts[0]) is { } exact
                => new PluginVersionRange(exact, true, exact, true),
            2 => Interval(parts[0], minInclusive, parts[1], maxInclusive),
            _ => null,
        };
    }

    public bool Contains(PluginSemVer version)
    {
        if (_min is not null)
        {
            var byMin = version.CompareTo(_min);
            if (byMin < 0 || (byMin == 0 && !_minInclusive))
                return false;
        }

        if (_max is not null)
        {
            var byMax = version.CompareTo(_max);
            if (byMax > 0 || (byMax == 0 && !_maxInclusive))
                return false;
        }

        return true;
    }

    private static PluginVersionRange? Interval(string low, bool minInclusive, string high, bool maxInclusive)
    {
        var min = string.IsNullOrWhiteSpace(low) ? null : PluginSemVer.TryParse(low);
        var max = string.IsNullOrWhiteSpace(high) ? null : PluginSemVer.TryParse(high);
        if ((min is null && !string.IsNullOrWhiteSpace(low)) || (max is null && !string.IsNullOrWhiteSpace(high)))
            return null;

        if (min is not null && max is not null)
        {
            var order = min.CompareTo(max);
            if (order > 0 || (order == 0 && !(minInclusive && maxInclusive)))
                return null;
        }

        return new PluginVersionRange(min, minInclusive, max, maxInclusive);
    }
}
