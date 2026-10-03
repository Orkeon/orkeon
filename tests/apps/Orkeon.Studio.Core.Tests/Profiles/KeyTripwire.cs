using System.Text.Json.Nodes;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// The tripwire against a key landing in a file Studio writes, by the file's structure
/// (STUDIO-49). It used to refuse the substring <c>ApiKey</c> anywhere in the text; a file now
/// names the variable holding each key (<c>ApiKeyEnvVar</c>), so the check reads the tree
/// instead: no property named <c>ApiKey</c>, at any depth, and every <c>ApiKeyEnvVar</c> the name
/// of a variable some setting carries — never a key pasted in its place. One <c>ApiKey</c> passes:
/// the placeholder <c>not-needed</c> exactly, which is no key — the one a Docker Model Runner
/// setting writes, as <c>orkeon init</c> does (STUDIO-54).
/// </summary>
internal static class KeyTripwire
{
    /// <summary>The placeholder Docker Model Runner's card writes: no key, and the only <c>ApiKey</c> allowed.</summary>
    private const string Placeholder = "not-needed";

    /// <summary>Asserts <paramref name="json"/> holds no key, and names only <paramref name="variables"/>.</summary>
    public static void AssertNamesNoKey(string json, params string[] variables)
    {
        var references = new List<string>();
        Walk(JsonNode.Parse(json), references);

        Assert.All(references, reference => Assert.Contains(reference, variables));
    }

    private static void Walk(JsonNode? node, List<string> references)
    {
        switch (node)
        {
            case JsonObject container:
                foreach (var (name, value) in container)
                {
                    if (string.Equals(name, "ApiKey", StringComparison.OrdinalIgnoreCase))
                        Assert.True(IsPlaceholder(value), $"A key was written into the file (property '{name}').");
                    if (string.Equals(name, "ApiKeyEnvVar", StringComparison.OrdinalIgnoreCase))
                        references.Add(Assert.IsType<string>(value!.GetValue<string>()));
                    else
                        Walk(value, references);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                    Walk(item, references);
                break;
        }
    }

    private static bool IsPlaceholder(JsonNode? value) =>
        value is JsonValue text && text.TryGetValue<string>(out var key) && string.Equals(key, Placeholder, StringComparison.Ordinal);
}
