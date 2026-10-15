using System.Globalization;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Reads the sampling values of <c>Microsoft.Extensions.AI.ChatOptions</c> (floats) back as the
/// decimal their author wrote.
/// </summary>
public static class LlmChatOptionsValues
{
    /// <summary>Reads a float as its shortest round-trip decimal (<c>0.7f</c> gives <c>0.7</c>).</summary>
    /// <param name="value">The float, or null.</param>
    /// <returns>The decimal as written, or null when <paramref name="value"/> is null.</returns>
    public static double? AsWritten(float? value) => value is null
        ? null
        : double.Parse(value.Value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
