using System.Globalization;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Decision;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Refuses a threshold of <c>Orkeon:Guardian:Llm</c> outside [0, 1]: one sentence per key, naming
/// it and the value it holds, written in the invariant culture (#456) — one sentence for both keys
/// said neither which one was wrong nor what it held.
/// </summary>
internal sealed class GuardianLlmThresholdsValidator : IValidateOptions<GuardianOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GuardianOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Check(failures, nameof(GuardianLlmOptions.BlockThreshold), options.Llm.BlockThreshold);
        Check(failures, nameof(GuardianLlmOptions.WarnThreshold), options.Llm.WarnThreshold);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(List<string> failures, string key, double value)
    {
        if (value is >= 0 and <= 1)
            return;

        failures.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{GuardianLlmOptions.SectionName}:{key}: {value} is not a probability between 0 and 1."));
    }
}
