using System.Net;
using System.Text;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// A token endpoint that issues <c>token-1</c>, <c>token-2</c>... one per grant request. A test holds
/// the answers back with <see cref="Gate"/> to make several callers wait on the same refresh, and
/// reads the forms it received.
/// </summary>
internal sealed class StubTokenEndpointHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<Dictionary<string, string>> _forms = [];
    private int _issued;

    /// <summary>Seconds the access tokens live, as <c>expires_in</c>.</summary>
    public int ExpiresIn { get; set; } = 3600;

    /// <summary>When set, answers wait until it completes.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Computes the answer instead of issuing a token (an error answer, for instance).</summary>
    public Func<Dictionary<string, string>, (HttpStatusCode Status, string Body)>? Respond { get; set; }

    /// <summary>Whether a refresh token is issued with each access token.</summary>
    public bool IssueRefreshToken { get; set; }

    /// <summary>The forms received, in order.</summary>
    public IReadOnlyList<Dictionary<string, string>> Forms
    {
        get { lock (_gate) return [.. _forms]; }
    }

    /// <summary>The number of requests received.</summary>
    public int Count
    {
        get { lock (_gate) return _forms.Count; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0].Replace('+', ' ')), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : ""));
        lock (_gate)
            _forms.Add(form);

        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        (HttpStatusCode Status, string Body) answer;
        if (Respond is { } respond)
        {
            answer = respond(form);
        }
        else
        {
            var n = Interlocked.Increment(ref _issued);
            var refresh = IssueRefreshToken ? $",\"refresh_token\":\"refresh-{n}\"" : "";
            answer = (HttpStatusCode.OK, $"{{\"access_token\":\"token-{n}\",\"token_type\":\"Bearer\",\"expires_in\":{ExpiresIn}{refresh}}}");
        }

        return new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") };
    }
}
