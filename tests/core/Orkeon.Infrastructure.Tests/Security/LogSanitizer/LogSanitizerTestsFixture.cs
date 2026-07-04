using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class LogSanitizerTestsFixture
{
    // --- Execution ---

    public static string? Sanitize(string? input)
        => LogSanitizer.Sanitize(input);
}
