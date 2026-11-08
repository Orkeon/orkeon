using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings.Providers;

/// <summary>Helpers shared by the OpenAI-compatible embedding provider tests (FWK-25).</summary>
internal static class EmbeddingHttpFixtures
{
    /// <summary>An OpenAI-shaped embeddings answer: <paramref name="count"/> vectors of <paramref name="dimensions"/> floats, listed in reverse index order.</summary>
    public static string Answer(int count, int dimensions)
    {
        var items = new List<string>();
        for (var i = count - 1; i >= 0; i--)
        {
            var values = string.Join(",", Enumerable.Range(0, dimensions)
                .Select(d => (i + (d / 1000.0)).ToString("0.###", CultureInfo.InvariantCulture)));
            items.Add($"{{\"object\":\"embedding\",\"index\":{i},\"embedding\":[{values}]}}");
        }
        return "{\"object\":\"list\",\"data\":[" + string.Join(",", items) + "]}";
    }

    public static MockHttpMessageHandler Handler(int count, int dimensions)
    {
        var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.OK, Answer(count, dimensions), "application/json");
        return handler;
    }

    public static IOptions<EmbeddingOptions> Options(Action<EmbeddingOptions> configure)
    {
        var options = new EmbeddingOptions();
        configure(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }
}
