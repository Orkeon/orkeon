using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Tools.Abstractions.Security;

namespace Orkeon.Infrastructure.Tests.Security;

public class HttpHeaderSanitizerTestsFixture
{
    private readonly HttpHeaderSanitizer _sanitizer = new(NullLogger<HttpHeaderSanitizer>.Instance);

    // --- Execution ---

    public HeaderSanitizationResult SanitizeHeaders(Dictionary<string, string>? headers)
        => _sanitizer.SanitizeHeaders(headers!);

    // --- Inspection ---

    public HttpHeaderSanitizer GetSanitizer() => _sanitizer;
}
