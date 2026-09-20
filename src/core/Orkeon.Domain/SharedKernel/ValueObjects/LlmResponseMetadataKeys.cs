namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// The keys every provider writes under <see cref="LlmResponse.Metadata"/> when a call fails
/// before it produces an answer. Declared once here so the code that writes them (the
/// providers' metadata builder) and the code that reads them (the agent loops, the chat client
/// adapter) name the same strings without a layering dependency between them (LLM-11).
/// </summary>
public static class LlmResponseMetadataKeys
{
    /// <summary>
    /// The provider's own account of the failure — the HTTP error, the elapsed timeout, the
    /// rejected request. Absent from every answer, including an empty one.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// The CLR type name of the exception behind <see cref="Error"/>
    /// (<c>TaskCanceledException</c> for an HTTP timeout, <c>HttpRequestException</c> for a
    /// refused request).
    /// </summary>
    public const string ErrorType = "error_type";
}
