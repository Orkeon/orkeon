using System.Text.RegularExpressions;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// What a cassette never keeps, shape by shape: bearer tokens, vendor and Google keys, a credential
/// in the query of a URL, user info in a URL. The same patterns shape the key of a call, so that an
/// answer stored in its redacted form is asked for under the key it was recorded with.
/// </summary>
internal static partial class CassetteRedaction
{
    /// <summary>The mark that stands for a removed value.</summary>
    public const string Mark = "[REDACTED]";

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{16,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex VendorKey();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_\-]{35}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex GoogleKey();

    [GeneratedRegex(@"([?&](?:key|api_key|apikey|token|access_token)=)[^&\s""'<>]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex UrlQueryCredential();

    [GeneratedRegex(@"(https?://)[^/\s@""'<>]+@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex UrlUserInfo();

    /// <summary>The text with every secret-shaped value replaced by <see cref="Mark"/>.</summary>
    public static string Text(string text)
    {
        text = BearerToken().Replace(text, "Bearer " + Mark);
        text = UrlQueryCredential().Replace(text, "$1" + Mark);
        text = UrlUserInfo().Replace(text, "$1" + Mark + "@");
        text = GoogleKey().Replace(text, Mark);
        return VendorKey().Replace(text, Mark);
    }
}
