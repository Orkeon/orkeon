using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Services;

/// <summary>The setting <c>Orkeon:Cli:Tui:Theme</c>: what it may hold, and the refusal of anything else at the start.</summary>
internal static class TuiThemeSetting
{
    /// <summary>The palette <paramref name="value"/> names, whatever its case; null when it names none.</summary>
    public static ConsoleTheme? Parse(string? value)
    {
        return Enum.GetValues<ConsoleTheme>()
            .Cast<ConsoleTheme?>()
            .FirstOrDefault(theme => string.Equals(theme.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Refuses a value of <paramref name="path"/> that is present and names no palette.</summary>
    /// <exception cref="OptionsValidationException">The value names no palette.</exception>
    public static void Judge(IServiceProvider services, string path)
    {
        var value = services.GetRequiredService<IConfiguration>()[path];
        if (string.IsNullOrEmpty(value) || Parse(value) is not null) return;
        throw new OptionsValidationException(
            path,
            typeof(ConsoleTheme),
            [$"{path} is '{value}', which is not a palette: write one of {string.Join(", ", Enum.GetNames<ConsoleTheme>())}."]);
    }
}
