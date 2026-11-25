using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests;

/// <summary>Files in the bot's personal chat, over a real listener and a fake connector: received, sent by consent, refused.</summary>
public sealed class TeamsFilesTests : IAsyncDisposable
{
    private const string FileType = "application/vnd.microsoft.teams.file.download.info";
    private const string ConsentType = "application/vnd.microsoft.teams.card.file.consent";
    private const string InfoType = "application/vnd.microsoft.teams.card.file.info";
    private const string OneDrive = "https://contoso-my.sharepoint.com";
    private const string Download = OneDrive + "/personal/u/_layouts/15/download.aspx?UniqueId='u-1'&x=%27%41&translate=false";
    private const string Temporary = OneDrive + "/download.aspx?TempAuth=v1.marker";
    private const string Upload = OneDrive + "/personal/u/_api/v2.0/drive/items/01/uploadSession?guid='7'&overwrite=True";
    private const string ImageAddress = "https://smba.trafficmanager.net/emea/v3/attachments/0/views/original";
    private const string OtherUser = "12345678-8888-7777-6666-555555555555";
    private static readonly DateTimeOffset s_before = new(2027, 2, 28, 23, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_after = new(2027, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly RecordingLoggerProvider _logs = new();
    private readonly ConcurrentQueue<InboundMessage> _received = new();
    private readonly ConcurrentQueue<CommandInvocation> _commands = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _client = new();
    private readonly string _pem = Pem();
    private Func<CommandInvocation, CommandReply> _answer = _ => new CommandReply("Answered.");
    private FakeBotConnector _microsoft = null!;
    private ILoggerFactory _loggers = null!;
    private TeamsChannel _channel = null!;
    private Task _running = Task.CompletedTask;
    private Uri _address = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Pem()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=bot", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddYears(200));
        return certificate.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem();
    }

    private async Task StartAsync(bool files = true, bool certificate = true, DateTimeOffset? now = null, params string[] names)
    {
        _microsoft = new FakeBotConnector(now);
        _microsoft.SignInFor = request =>
        {
            var token = request.Form["scope"] == "https://api.botframework.com/.default" ? "bot" : "file";
            return FakeHttpResponse.Json($$"""{"token_type":"Bearer","expires_in":3600,"access_token":"{{token}}"}""");
        };
        _loggers = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        (_channel, var port) = await LoopbackPorts.StartAsync(
            p =>
            {
                var options = _microsoft.Options(LoopbackPorts.BaseAddress(p).AbsoluteUri) with
                {
                    SharePointNames = names.Length == 0 ? ["contoso"] : [.. names.Where(n => n.Length > 0)],
                    AllowedUserIds = [Activities.UserId, OtherUser],
                };
                return new TeamsChannel(
                    Options.Create(options),
                    _loggers.CreateLogger<TeamsChannel>(),
                    _microsoft.Time,
                    _microsoft.Transport,
                    files,
                    certificate ? _ => Task.FromResult(_pem) : null);
            },
            (channel, _) =>
            {
                channel.Bind();
                return Task.CompletedTask;
            });
        _channel.CommandInvoked += invocation =>
        {
            _commands.Enqueue(invocation);
            return Task.FromResult(_answer(invocation));
        };
        _running = _channel.RunAsync(
            (message, _) =>
            {
                _received.Enqueue(message);
                return Task.CompletedTask;
            },
            _stop.Token);
        _address = new Uri(LoopbackPorts.BaseAddress(port), "api/messages");
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running;
        if (_channel is not null)
            await _channel.DisposeAsync();
        _stop.Dispose();
        _client.Dispose();
        _loggers?.Dispose();
        _logs.Dispose();
        _microsoft?.Dispose();
    }

    private async Task<HttpStatusCode> PostAsync(Dictionary<string, object?> activity, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _address);
        request.Content = new StringContent(JsonSerializer.Serialize(activity), Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? _microsoft.Token());
        using var response = await _client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private async Task<TeamsInboundMessage> NextAsync(Dictionary<string, object?> activity)
    {
        var before = _received.Count;
        Assert.Equal(HttpStatusCode.OK, await PostAsync(activity));
        await Polling.WaitUntilAsync(() => _received.Count > before, Polling.DefaultTimeout);
        return Assert.IsType<TeamsInboundMessage>(_received.Last());
    }

    /// <summary>Waits for one more reply of the bot and reads it.</summary>
    private async Task<JsonElement> ReplyAsync(int index)
    {
        await Polling.WaitUntilAsync(() => _microsoft.Replies.Count > index, Polling.DefaultTimeout);
        return JsonSerializer.Deserialize<JsonElement>(_microsoft.Replies[index].Body!);
    }

    private async Task<string> TextAsync(int index) => (await ReplyAsync(index)).GetProperty("text").GetString()!;

    private static object File(string address, string? tenant = FakeBotConnector.TenantId, string name = "march.pdf") => new
    {
        contentType = FileType,
        name,
        contentUrl = OneDrive + "/march.pdf",
        content = new { uniqueId = "u-1", fileType = "pdf", downloadUrl = address, tenantId = tenant },
    };

    private static object Image(string address) => new { contentType = "image/png", contentUrl = address };

    private static async Task<string> ReadAsync(InboundMessage message, int index = 0)
    {
        var source = message.AttachmentSource;
        Assert.NotNull(source);
        await using var stream = await source.OpenAsync(message.Attachments[index], Ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(Ct);
    }

    private static async Task<string> RefusedAsync(InboundMessage message, int index = 0) =>
        (await Assert.ThrowsAsync<AttachmentRefusedException>(() => ReadAsync(message, index))).Message;

    private IReadOnlyList<RecordedRequest> Elsewhere => [.. _microsoft.Transport.Requests.Where(r =>
        !r.Uri.AbsoluteUri.StartsWith("https://smba.trafficmanager.net/", StringComparison.Ordinal)
        && !r.Uri.AbsoluteUri.StartsWith("https://login.", StringComparison.Ordinal))];

    private IReadOnlyList<RecordedRequest> FileSignIns => [.. _microsoft.SignIns.Where(r => r.Form["scope"] != "https://api.botframework.com/.default")];

    private void AssertNothingLeaks(params string[] values)
    {
        var said = string.Join('\n', _logs.Logger.Entries) + string.Join('\n', _received) + string.Concat(_received.SelectMany(m => m.Attachments));
        foreach (var value in values)
            Assert.DoesNotContain(value, said, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Received ------------------------------------------------------------------------

    [Fact]
    public async Task A_file_of_a_personal_chat_is_read_at_its_exact_address_with_a_token_for_its_host()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Get, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "hello", "application/pdf"));

        var message = await NextAsync(Activities.Personal("read it", "1", File(Download)));

        Assert.Equal("hello", await ReadAsync(message));
        var request = Assert.Single(Elsewhere);
        Assert.Equal(Download, request.Uri.AbsoluteUri);
        Assert.Equal("Bearer file", request.Authorization);
        var form = Assert.Single(FileSignIns).Form;
        Assert.Equal(OneDrive + "/.default", form["scope"]);
        Assert.Equal(FakeBotConnector.AppId, form["client_id"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", form["client_assertion_type"]);
        Assert.False(form.ContainsKey("client_secret"));
        Assert.Contains(FakeBotConnector.TenantId, FileSignIns[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Null(message.NoFilesHere);

        // The token is kept for its host.
        Assert.Equal("hello", await ReadAsync(message));
        Assert.Single(FileSignIns);
        AssertNothingLeaks("download.aspx", "Bearer", form["client_assertion"]);
    }

    [Fact]
    public async Task Each_message_names_its_files_its_own_way()
    {
        await StartAsync();
        var first = await NextAsync(Activities.Personal("a", "1", Image(ImageAddress), File(Download)));
        var second = await NextAsync(Activities.Personal("b", "2", Image(ImageAddress), File(Download)));

        Assert.Equal(4, first.Attachments.Concat(second.Attachments).Select(a => a.Id).Distinct().Count());
        Assert.Equal(["image", "march.pdf"], first.Attachments.Select(a => a.Name));
    }

    [Theory]
    [InlineData("https://fabrikam-my.sharepoint.com/f?tempauth=1")]
    [InlineData("https://contoso-my.sharepoint.com.evil.example/f?tempauth=1")]
    [InlineData("https://evilcontoso-my.sharepoint.com/f?tempauth=1")]
    [InlineData("https://evilcontoso.sharepoint.com/f?tempauth=1")]
    [InlineData("https://x.contoso.sharepoint.com/f?tempauth=1")]
    [InlineData("https://contoso.sharepoint.com@evil.example/f?tempauth=1")]
    [InlineData("https://evil.example/contoso-my.sharepoint.com?tempauth=1")]
    [InlineData("https://evil.example\\.contoso-my.sharepoint.com/?tempauth=1")]
    [InlineData("http://contoso-my.sharepoint.com/f?tempauth=1")]
    [InlineData("https://contoso-my.sharepoint.com:8443/f?tempauth=1")]
    [InlineData("https://u@contoso-my.sharepoint.com/f?tempauth=1")]
    [InlineData("https://contoso-my.sharepoint.com/f?tempauth=1\r\nX: y")]
    [InlineData("https://contoso-my.sharepoint.com/f?tempauth=1#x")]
    [InlineData("https://graph.microsoft.com/f?tempauth=1")]
    [InlineData("https://127.0.0.1/f?tempauth=1")]
    [InlineData("/f?tempauth=1")]
    [InlineData("")]
    public async Task A_file_elsewhere_is_refused_and_nothing_is_asked_for_it(string address)
    {
        await StartAsync(now: s_before);

        var refusal = await RefusedAsync(await NextAsync(Activities.Personal("x", "1", File(address))));

        Assert.Contains("SharePoint", refusal, StringComparison.Ordinal);
        Assert.Empty(Elsewhere);
        Assert.Empty(FileSignIns);
    }

    [Fact]
    public async Task A_file_of_another_tenant_is_refused()
    {
        await StartAsync();
        await RefusedAsync(await NextAsync(Activities.Personal("x", "1", File(Download, "00000000-bbbb-cccc-dddd-eeeeeeeeeeee"))));
        Assert.Empty(Elsewhere);
        Assert.Empty(FileSignIns);
    }

    [Fact]
    public async Task A_temporary_address_goes_without_a_token_until_the_date_Microsoft_gives()
    {
        await StartAsync(certificate: false, now: s_before);
        _microsoft.Transport.Map(HttpMethod.Get, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "hello"));
        var message = await NextAsync(Activities.Personal("x", "1", File(Temporary)));

        Assert.Equal("hello", await ReadAsync(message));
        Assert.Null(Assert.Single(Elsewhere).Authorization);

        _microsoft.Time.Advance(s_after - s_before);
        Assert.Contains("certificate", await RefusedAsync(message), StringComparison.Ordinal);
        Assert.Single(Elsewhere);
        Assert.Empty(FileSignIns);
        AssertNothingLeaks("marker");
    }

    [Fact]
    public async Task An_address_that_is_not_temporary_needs_a_token()
    {
        await StartAsync(certificate: false, now: s_before);
        Assert.Contains("certificate", await RefusedAsync(await NextAsync(Activities.Personal("x", "1", File(Download)))), StringComparison.Ordinal);
        Assert.Contains("certificate", await RefusedAsync(await NextAsync(Activities.Personal("x", "2", File(OneDrive + "/f?x=tempauth=1")))), StringComparison.Ordinal);
        Assert.Empty(Elsewhere);
    }

    [Fact]
    public async Task A_sign_in_that_fails_falls_back_on_a_temporary_address_alone()
    {
        await StartAsync(now: s_before);
        var bot = _microsoft.SignInFor!;
        _microsoft.SignInFor = r => r.Form.ContainsKey("client_assertion")
            ? new FakeHttpResponse(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"marker"}""") : bot(r);
        _microsoft.Transport.Map(HttpMethod.Get, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "hello"));

        var temporary = await NextAsync(Activities.Personal("x", "1", File(Temporary)));
        Assert.Equal("hello", await ReadAsync(temporary));
        Assert.Null(Assert.Single(Elsewhere).Authorization);
        Assert.Contains("sign", await RefusedAsync(await NextAsync(Activities.Personal("x", "2", File(Download)))), StringComparison.Ordinal);
        Assert.Single(Elsewhere);

        // Past the date, the temporary address is worth no more than another.
        _microsoft.Time.Advance(s_after - s_before);
        Assert.Contains("sign", await RefusedAsync(temporary), StringComparison.Ordinal);
        Assert.Single(Elsewhere);
        AssertNothingLeaks("marker");
    }

    [Theory]
    [InlineData("/personal/u/Documents/q%201/a%2Bb.pdf")]
    [InlineData("/sites/team/Shared%20Documents/%C3%A9t%C3%A9%20100%25.pdf")]
    [InlineData("/personal/u/_layouts/15/download.aspx?UniqueId=1")]
    [InlineData("/personal/u/_api/v2.0/drive/items/01/content?x=1")]
    [InlineData("/_api/v2.1/drives/b!1/items/01/content")]
    public async Task The_paths_a_file_is_at_are_read_as_they_are_written(string path)
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Get, "https://contoso", new FakeHttpResponse(HttpStatusCode.OK, "hello"));
        foreach (var host in new[] { OneDrive, "https://contoso.sharepoint.com" })
        {
            Assert.Equal("hello", await ReadAsync(await NextAsync(Activities.Personal("x", host, File(host + path)))));
            Assert.Equal(host + path, Elsewhere[^1].Uri.AbsoluteUri);
        }
    }

    // The token is the application's: what it may read beyond files is not a file anyone shared.
    [Theory]
    [InlineData("/personal/u/_api/web/lists")]
    [InlineData("/_API/web/siteusers")]
    [InlineData("/_api/v2.0/drive/items/01/children")]
    [InlineData("/_api/v2.0/sites/root/content")]
    [InlineData("/_api/v2.0/drive/items/_api/web/content")]
    [InlineData("/_api/web/drive/items/01/content")]
    [InlineData("/personal/u/%5Fapi/web/lists")]
    [InlineData("/personal/u/_api./web/lists")]
    [InlineData("/personal/u/_api%20/web/lists")]
    [InlineData("/_vti_bin/client.svc/ProcessQuery")]
    [InlineData("/_vti_history/512/a.docx")]
    [InlineData("/_layouts/15/viewlsts.aspx")]
    [InlineData("/_layouts/15/download.aspx/x")]
    [InlineData("/_layouts/15/../15/download.aspx")]
    [InlineData("/_catalogs/masterpage/a.master")]
    [InlineData("/_admin/a.aspx")]
    [InlineData("/personal/u/../../sites/secret/a.pdf")]
    [InlineData("/personal/u/%2E%2e/a.pdf")]
    [InlineData("/personal/u/.../a.pdf")]
    [InlineData("/personal/u/%2e%2e%2fa.pdf")]
    [InlineData("/personal/u%5ca.pdf")]
    [InlineData("/personal/u/%252e%252e/a.pdf")]
    [InlineData("/personal/u/%c0%ae/a.pdf")]
    [InlineData("/personal/u/%00/a.pdf")]
    [InlineData("/personal//u/a.pdf")]
    [InlineData("/personal/u/")]
    [InlineData("/")]
    [InlineData("")]
    public async Task A_path_that_is_no_file_is_refused_on_the_tenants_own_hosts_and_no_token_is_asked(string path)
    {
        await StartAsync(now: s_before);
        foreach (var host in new[] { OneDrive, "https://contoso.sharepoint.com" })
        {
            var message = await NextAsync(Activities.Personal("x", Guid.NewGuid().ToString("N"), File(host + path + "?tempauth=1")));
            Assert.Contains("SharePoint", await RefusedAsync(message), StringComparison.Ordinal);
        }

        Assert.Empty(Elsewhere);
        Assert.Empty(FileSignIns);
    }

    [Theory]
    [InlineData(8192, true)]
    [InlineData(8193, false)]
    public async Task An_address_has_a_length(int length, bool read)
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Get, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "hello"));
        var message = await NextAsync(Activities.Personal("x", "1", File((OneDrive + "/f?x=").PadRight(length, 'a'))));

        if (read)
            Assert.Equal("hello", await ReadAsync(message));
        else
            await RefusedAsync(message);
        Assert.Equal(read ? 1 : 0, Elsewhere.Count);
    }

    [Fact]
    public async Task A_token_goes_to_the_host_it_was_asked_for_until_it_expires_or_is_refused()
    {
        await StartAsync();
        var asked = 0;
        _microsoft.SignInFor = r => FakeHttpResponse.Json($$"""{"token_type":"Bearer","expires_in":3600,"access_token":"{{new Uri(r.Form["scope"]).Host}}-{{++asked}}"}""");
        var status = HttpStatusCode.OK;
        _microsoft.Transport.Map(HttpMethod.Get, "https://contoso", _ => new FakeHttpResponse(status, "hello"));
        var mine = await NextAsync(Activities.Personal("x", "1", File(OneDrive + "/f")));
        var team = await NextAsync(Activities.Personal("x", "2", File("https://contoso.sharepoint.com/f")));

        await ReadAsync(mine);
        await ReadAsync(team);
        await ReadAsync(mine);
        _microsoft.Time.Advance(TimeSpan.FromHours(2));
        await ReadAsync(mine);
        status = HttpStatusCode.Unauthorized;
        await RefusedAsync(mine);
        status = HttpStatusCode.OK;
        await ReadAsync(mine);

        Assert.Equal(
            [
                "Bearer contoso-my.sharepoint.com-1", "Bearer contoso.sharepoint.com-2", "Bearer contoso-my.sharepoint.com-1",
                "Bearer contoso-my.sharepoint.com-3", "Bearer contoso-my.sharepoint.com-3", "Bearer contoso-my.sharepoint.com-4",
            ],
            Elsewhere.Select(r => r.Authorization));
    }

    [Fact]
    public async Task A_sign_in_that_waits_for_one_host_holds_up_no_other()
    {
        await StartAsync();
        using var answered = new ManualResetEventSlim();
        _microsoft.SignInFor = r =>
        {
            if (r.Form["scope"] == OneDrive + "/.default")
                answered.Wait(Polling.DefaultTimeout, Ct);
            return FakeHttpResponse.Json("""{"token_type":"Bearer","expires_in":3600,"access_token":"file"}""");
        };
        _microsoft.Transport.Map(HttpMethod.Get, "https://contoso", new FakeHttpResponse(HttpStatusCode.OK, "hello"));
        var mine = await NextAsync(Activities.Personal("x", "1", File(OneDrive + "/f")));
        var team = await NextAsync(Activities.Personal("x", "2", File("https://contoso.sharepoint.com/f")));

        var slow = Task.Run(() => ReadAsync(mine), Ct);
        await Polling.WaitUntilAsync(() => FileSignIns.Count == 1, Polling.DefaultTimeout);
        try
        {
            Assert.Equal("hello", await ReadAsync(team).WaitAsync(Polling.DefaultTimeout / 2, Ct));
        }
        finally
        {
            answered.Set();
        }

        Assert.Equal("hello", await slow);
    }

    [Fact]
    public async Task A_transport_that_fails_says_nothing_of_the_address()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Get, OneDrive, _ => throw new HttpRequestException("marker " + Download));
        _microsoft.Transport.Map(HttpMethod.Get, ImageAddress, _ => throw new HttpRequestException("marker " + ImageAddress));
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, _ => throw new HttpRequestException("marker " + Upload));
        var message = await NextAsync(Activities.Personal("x", "1", File(Download), Image(ImageAddress)));

        foreach (var index in new[] { 0, 1 })
        {
            var refused = await Assert.ThrowsAsync<AttachmentRefusedException>(() => ReadAsync(message, index));
            Assert.Contains("could not be read", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("marker", refused.ToString(), StringComparison.Ordinal);
        }

        var fileId = await OfferAsync();
        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));
        var said = await TextAsync(1);
        Assert.Contains("SharePoint could not be reached", said, StringComparison.Ordinal);
        Assert.DoesNotContain("marker", said, StringComparison.Ordinal);
        AssertNothingLeaks("marker", "download.aspx", "uploadSession", "views/original");
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task Any_answer_but_200_is_no_file_and_no_redirection_is_followed(HttpStatusCode status)
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Get, OneDrive, new FakeHttpResponse(status, "marker", Location: new Uri("https://evil.example/x")));

        await RefusedAsync(await NextAsync(Activities.Personal("x", "1", File(Download))));

        Assert.Single(Elsewhere);
        AssertNothingLeaks("download.aspx", "marker");
    }

    [Fact]
    public async Task An_inline_image_is_read_on_the_connector_with_the_bots_token()
    {
        await StartAsync(certificate: false);
        _microsoft.Transport.Map(HttpMethod.Get, ImageAddress, new FakeHttpResponse(HttpStatusCode.OK, "png", "image/png"));

        Assert.Equal("png", await ReadAsync(await NextAsync(Activities.Personal("x", "1", Image(ImageAddress)))));

        Assert.Equal("Bearer bot", Assert.Single(_microsoft.Transport.RequestsTo(HttpMethod.Get, ImageAddress)).Authorization);
        Assert.Empty(FileSignIns);
    }

    [Theory]
    [InlineData("https://evil.example/v3/attachments/0/views/original")]
    [InlineData("https://contoso-my.sharepoint.com/i.png")]
    [InlineData("http://smba.trafficmanager.net/emea/v3/attachments/0")]
    [InlineData("https://smba.trafficmanager.net.evil.example/v3/attachments/0")]
    [InlineData("https://evilsmba.trafficmanager.net/v3/attachments/0")]
    [InlineData("https://x.smba.trafficmanager.net/v3/attachments/0")]
    [InlineData("https://smba.infra.gcc.teams.microsoft.com/v3/attachments/0/views/original")]
    [InlineData("https://smba.trafficmanager.net/emea/v3/conversations/a/members")]
    [InlineData("https://smba.trafficmanager.net/emea/v3/attachments/../conversations/a/members")]
    [InlineData("https://smba.trafficmanager.net/emea/v3/attachments/%2e%2e/conversations/a/members")]
    [InlineData("https://smba.trafficmanager.net/emea/v3/attachments/0%2f..%2f..%2fconversations")]
    [InlineData("https://smba.trafficmanager.net/emea/v3/attachments/")]
    [InlineData("https://smba.trafficmanager.net/emea/v3/attachments")]
    [InlineData("https://smba.trafficmanager.net/emea?x=/v3/attachments/0")]
    public async Task An_image_elsewhere_is_refused_and_the_bots_token_stays(string address)
    {
        await StartAsync();
        await RefusedAsync(await NextAsync(Activities.Personal("x", "1", Image(address))));
        Assert.Empty(Elsewhere);
        Assert.Empty(_microsoft.SignIns);
    }

    [Fact]
    public async Task Without_the_tenants_names_files_are_refused_and_the_start_says_so()
    {
        await StartAsync(names: [""]);
        Assert.Contains("SharePointNames", await RefusedAsync(await NextAsync(Activities.Personal("x", "1", File(Download)))), StringComparison.Ordinal);
        Assert.Contains(_logs.Logger.Entries, e => e.Message.Contains("Teams:SharePointNames", StringComparison.Ordinal));
        Assert.Empty(Elsewhere);
    }

    [Fact]
    public async Task Without_a_certificate_the_start_says_until_when_files_pass()
    {
        await StartAsync(certificate: false);
        await Polling.WaitUntilAsync(() => _logs.Logger.Entries.Any(e => e.Message.Contains("2027-03-01", StringComparison.Ordinal)), Polling.DefaultTimeout);
    }

    // ---- Where files are not taken ---------------------------------------------------------

    [Theory]
    [InlineData("channel")]
    [InlineData("groupChat")]
    public async Task Outside_a_personal_chat_files_and_their_commands_are_refused_in_words(string type)
    {
        await StartAsync();
        var activity = Activities.Message("look", "1");
        activity["conversation"] = new { id = Activities.Thread, conversationType = type, tenantId = FakeBotConnector.TenantId };
        activity["attachments"] = new[] { File(Download) };
        var message = await NextAsync(activity);

        Assert.Null(message.AttachmentSource);
        Assert.Contains("personal chat", message.NoFilesHere, StringComparison.Ordinal);

        activity["id"] = "2";
        activity["text"] = "<at>Orkeon</at> /get /outbox/a.txt";
        await PostAsync(activity);
        Assert.Contains("personal chat", await TextAsync(0), StringComparison.Ordinal);
        Assert.Empty(_commands);

        var results = await _channel.Responder.SendFilesAsync(message, [Outbound("x")], Ct);
        Assert.Equal(FileSendOutcome.Refused, Assert.Single(results).Outcome);
        Assert.Contains("personal chat", results[0].Reason, StringComparison.Ordinal);
        Assert.Single(_microsoft.Replies);
    }

    [Fact]
    public async Task Without_the_hosts_files_nothing_is_read_and_a_file_command_is_answered_by_the_host()
    {
        await StartAsync(files: false);
        var message = await NextAsync(Activities.Personal("x", "1", File(Download)));
        Assert.Null(message.AttachmentSource);
        Assert.Null(message.NoFilesHere);

        await PostAsync(Activities.Personal("/files", "2"));
        Assert.Equal("Answered.", await TextAsync(0));
        Assert.Equal(FileSendOutcome.Refused, Assert.Single(await _channel.Responder.SendFilesAsync(message, [Outbound("x")], Ct)).Outcome);
    }

    // ---- Commands --------------------------------------------------------------------------

    [Theory]
    [InlineData("/files", "files", "folder", null)]
    [InlineData("/FILES  /output/Reports ", "files", "folder", "/output/Reports")]
    [InlineData("/get /outbox/My report.PDF", "get", "path", "/outbox/My report.PDF")]
    [InlineData("/put /output/drop", "put", "folder", "/output/drop")]
    public async Task A_file_command_is_read_with_its_argument(string text, string command, string name, string? value)
    {
        await StartAsync();
        await PostAsync(Activities.Personal(text, "1", File(Download)));
        Assert.Equal("Answered.", await TextAsync(0));

        var invocation = Assert.Single(_commands);
        Assert.Equal(command, invocation.CommandName);
        Assert.Equal(Activities.PersonalChat, invocation.ConversationId);
        Assert.Equal(Activities.PersonalChat, invocation.RoomId);
        Assert.Equal(value, invocation.Arguments.GetValueOrDefault(name));
        Assert.Equal("march.pdf", Assert.Single(invocation.Attachments).Name);
        Assert.NotNull(invocation.AttachmentSource);
        Assert.Empty(_received);
    }

    [Theory]
    [InlineData("/getaway ideas")]
    [InlineData("explain /files")]
    public async Task What_only_resembles_a_command_is_a_prompt(string text)
    {
        await StartAsync();
        Assert.Equal(text, (await NextAsync(Activities.Personal(text, "1"))).Text);
        Assert.Empty(_commands);
    }

    // ---- Sent by consent -------------------------------------------------------------------

    private static OutboundFile Outbound(string content, string name = "report.txt", long? size = null) => new()
    {
        Name = name,
        Size = size ?? Encoding.UTF8.GetByteCount(content),
        OpenAsync = _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content))),
    };

    private static object UploadInfo(string address = Upload, string? tenant = FakeBotConnector.TenantId) => new
    {
        uploadUrl = address,
        contentUrl = OneDrive + "/personal/u/Documents/report.txt",
        uniqueId = "item-1",
        fileType = "txt",
        tenantId = tenant,
    };

    private Task<TeamsInboundMessage> MessageAsync(string id = "m1") => NextAsync(Activities.Personal("send it", id));

    /// <summary>Sends a file, and reads the identifier its consent card carries.</summary>
    private async Task<string> OfferAsync(OutboundFile? file = null)
    {
        var message = await MessageAsync($"m{Guid.NewGuid():N}");
        var index = _microsoft.Replies.Count;
        var result = Assert.Single(await _channel.Responder.SendFilesAsync(message, [file ?? Outbound("hello")], Ct));
        Assert.Equal(FileSendOutcome.AwaitingConsent, result.Outcome);
        return Card(await ReplyAsync(index), ConsentType).GetProperty("content").GetProperty("acceptContext").GetProperty("fileId").GetString()!;
    }

    private static JsonElement Card(JsonElement activity, string type)
    {
        var card = Assert.Single(activity.GetProperty("attachments").EnumerateArray());
        Assert.Equal(type, card.GetProperty("contentType").GetString());
        return card;
    }

    private IReadOnlyList<RecordedRequest> Puts => [.. _microsoft.Transport.Requests.Where(r => r.Method == HttpMethod.Put)];

    [Fact]
    public async Task An_accepted_file_is_put_where_Teams_says_and_shown()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.Created, "{}"));
        var message = await MessageAsync();

        var result = Assert.Single(await _channel.Responder.SendFilesAsync(message, [Outbound("hello")], Ct));

        Assert.Equal(FileSendOutcome.AwaitingConsent, result.Outcome);
        var offered = Card(await ReplyAsync(0), ConsentType);
        Assert.Equal("report.txt", offered.GetProperty("name").GetString());
        var content = offered.GetProperty("content");
        Assert.Equal(5, content.GetProperty("sizeInBytes").GetInt64());
        var fileId = content.GetProperty("acceptContext").GetProperty("fileId").GetString()!;
        Assert.Equal(fileId, content.GetProperty("declineContext").GetProperty("fileId").GetString());
        Assert.Empty(Puts);

        Assert.Equal(HttpStatusCode.OK, await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo())));
        var shown = Card(await ReplyAsync(1), InfoType);

        var put = Assert.Single(Puts);
        Assert.Equal(Upload, put.Uri.AbsoluteUri);
        Assert.Equal("Bearer file", put.Authorization);
        Assert.Equal("hello", put.Body);
        Assert.Equal("application/octet-stream", put.ContentType);
        Assert.Equal("bytes 0-4/5", put.ContentRange);
        Assert.Equal(OneDrive + "/personal/u/Documents/report.txt", shown.GetProperty("contentUrl").GetString());
        Assert.Equal("report.txt", shown.GetProperty("name").GetString());
        Assert.Equal("item-1", shown.GetProperty("content").GetProperty("uniqueId").GetString());
        Assert.Equal("txt", shown.GetProperty("content").GetProperty("fileType").GetString());
        Assert.Single(_received);
        AssertNothingLeaks("uploadSession", "Bearer", fileId);
    }

    [Fact]
    public async Task A_declined_file_is_not_sent_and_cannot_be_accepted_after()
    {
        await StartAsync();
        var fileId = await OfferAsync();

        await PostAsync(Activities.Consent(fileId, "i1", "decline"));
        Assert.Contains("declined", await TextAsync(1), StringComparison.Ordinal);
        await PostAsync(Activities.Consent(fileId, "i2", uploadInfo: UploadInfo()));
        Assert.Contains("no longer", await TextAsync(2), StringComparison.Ordinal);
        Assert.Empty(Puts);
    }

    [Fact]
    public async Task A_request_expires()
    {
        await StartAsync();
        var fileId = await OfferAsync();
        _microsoft.Time.Advance(TimeSpan.FromMinutes(61));

        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));

        Assert.Contains("no longer", await TextAsync(1), StringComparison.Ordinal);
        Assert.Empty(Puts);
    }

    [Fact]
    public async Task A_request_this_host_does_not_hold_is_refused_and_the_restart_is_named()
    {
        await StartAsync();
        await PostAsync(Activities.Consent(Guid.NewGuid().ToString("N"), "i1", uploadInfo: UploadInfo()));
        Assert.Contains("restart", await TextAsync(0), StringComparison.Ordinal);
        Assert.Empty(Puts);
    }

    [Fact]
    public async Task A_replayed_acceptance_sends_nothing_more()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "{}"));
        var fileId = await OfferAsync();
        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));
        Card(await ReplyAsync(1), InfoType);

        // Delivered again: the same activity, then the same answer in a new one.
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo())));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Activities.Consent(fileId, "i2", uploadInfo: UploadInfo())));

        Assert.Contains("no longer", await TextAsync(2), StringComparison.Ordinal);
        Assert.Equal(3, _microsoft.Replies.Count);
        Assert.Single(Puts);
    }

    [Fact]
    public async Task Another_person_or_conversation_cannot_answer_and_takes_nothing_from_who_can()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "{}"));
        var fileId = await OfferAsync();

        var other = Activities.Consent(fileId, "i1", uploadInfo: UploadInfo());
        other["from"] = new { id = "29:other", aadObjectId = OtherUser };
        await PostAsync(other);
        Assert.Contains("no longer", await TextAsync(1), StringComparison.Ordinal);

        var elsewhere = Activities.Consent(fileId, "i2", "decline");
        elsewhere["conversation"] = new { id = "a:another", conversationType = "personal", tenantId = FakeBotConnector.TenantId };
        await PostAsync(elsewhere);
        Assert.Contains("no longer", await TextAsync(2), StringComparison.Ordinal);
        Assert.Empty(Puts);

        await PostAsync(Activities.Consent(fileId, "i3", uploadInfo: UploadInfo()));
        Card(await ReplyAsync(3), InfoType);
        Assert.Single(Puts);
    }

    [Fact]
    public async Task An_acceptance_from_a_stranger_or_without_Microsofts_signature_is_refused()
    {
        await StartAsync();
        var fileId = await OfferAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()), _microsoft.Token(signedWith: _microsoft.AnotherKey())));
        var tenant = Activities.Consent(fileId, "i2", uploadInfo: UploadInfo());
        tenant["channelData"] = new { tenant = new { id = "00000000-bbbb-cccc-dddd-eeeeeeeeeeee" } };
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(tenant));

        var stranger = Activities.Consent(fileId, "i3", uploadInfo: UploadInfo());
        stranger["from"] = new { id = "29:stranger", aadObjectId = "00000000-8888-7777-6666-555555555555" };
        await PostAsync(stranger);
        Assert.Contains("not authorized", await TextAsync(1), StringComparison.Ordinal);
        Assert.Empty(Puts);
        Assert.Empty(FileSignIns);
    }

    [Fact]
    public async Task An_acceptance_in_a_channel_is_refused()
    {
        await StartAsync();
        var fileId = await OfferAsync();
        var activity = Activities.Consent(fileId, "i1", uploadInfo: UploadInfo());
        activity["conversation"] = new { id = Activities.PersonalChat, conversationType = "channel", tenantId = FakeBotConnector.TenantId };
        await PostAsync(activity);
        Assert.Contains("personal chat", await TextAsync(1), StringComparison.Ordinal);
        Assert.Empty(Puts);
    }

    [Theory]
    [InlineData("https://evil.example/upload?tempauth=1", FakeBotConnector.TenantId)]
    [InlineData("http://contoso-my.sharepoint.com/upload?tempauth=1", FakeBotConnector.TenantId)]
    [InlineData("https://contoso-my.sharepoint.com.evil.example/personal/u/_api/v2.0/drive/items/01/uploadSession?tempauth=1", FakeBotConnector.TenantId)]
    [InlineData("https://evilcontoso-my.sharepoint.com/personal/u/_api/v2.0/drive/items/01/uploadSession?tempauth=1", FakeBotConnector.TenantId)]
    [InlineData("https://contoso.sharepoint.com/personal/u/_api/v2.0/drive/items/01/uploadSession?tempauth=1", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/Documents/report.docx", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/_api/v2.0/drive/items/01/content", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/_api/v2.0/drive/items/01?x=/uploadSession", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/_api/web/lists/uploadSession", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/uploadSession", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/_api/v2.0/drive/../../web/uploadSession", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/_api/v2.0/drive/items/01%2f..%2fuploadSession", FakeBotConnector.TenantId)]
    [InlineData(OneDrive + "/personal/u/_api/v2.0/drive/items/_vti_bin/uploadSession", FakeBotConnector.TenantId)]
    [InlineData(Upload, "00000000-bbbb-cccc-dddd-eeeeeeeeeeee")]
    public async Task An_upload_elsewhere_is_refused_and_no_token_is_asked(string address, string tenant)
    {
        await StartAsync(now: s_before);
        var fileId = await OfferAsync();

        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo(address, tenant)));

        Assert.Contains("was not sent", await TextAsync(1), StringComparison.Ordinal);
        Assert.Empty(Elsewhere);
        Assert.Empty(FileSignIns);
    }

    [Fact]
    public async Task An_acceptance_without_an_upload_address_sends_nothing()
    {
        await StartAsync();
        var fileId = await OfferAsync();
        await PostAsync(Activities.Consent(fileId, "i1"));
        Assert.Contains("was not sent", await TextAsync(1), StringComparison.Ordinal);
        Assert.Empty(Elsewhere);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Any_answer_to_the_upload_but_200_and_201_is_a_failure(HttpStatusCode status)
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(status, "marker", Location: new Uri("https://evil.example/")));
        var fileId = await OfferAsync();

        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));

        Assert.Contains("was not sent", await TextAsync(1), StringComparison.Ordinal);
        Assert.Single(Elsewhere);
        AssertNothingLeaks("marker", "uploadSession");
    }

    [Fact]
    public async Task What_an_upload_could_not_send_may_be_accepted_again_with_another_token()
    {
        await StartAsync();
        var asked = 0;
        _microsoft.SignInFor = _ => FakeHttpResponse.Json($$"""{"token_type":"Bearer","expires_in":3600,"access_token":"t{{++asked}}"}""");
        var status = HttpStatusCode.Unauthorized;
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, _ => new FakeHttpResponse(status, "{}"));
        var fileId = await OfferAsync();
        var bot = asked;

        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));
        Assert.Contains("within the hour", await TextAsync(1), StringComparison.Ordinal);
        await PostAsync(Activities.Consent(fileId, "i2", uploadInfo: UploadInfo("https://evil.example/")));
        Assert.Contains("within the hour", await TextAsync(2), StringComparison.Ordinal);
        status = HttpStatusCode.Created;
        await PostAsync(Activities.Consent(fileId, "i3", uploadInfo: UploadInfo()));
        Card(await ReplyAsync(3), InfoType);

        Assert.Equal([$"Bearer t{bot + 1}", $"Bearer t{bot + 2}"], Puts.Select(p => p.Authorization));
        await PostAsync(Activities.Consent(fileId, "i4", uploadInfo: UploadInfo()));
        Assert.Contains("no longer", await TextAsync(4), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_upload_gives_the_offer_no_longer_life()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.Forbidden, "{}"));
        var fileId = await OfferAsync();
        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));
        await TextAsync(1);
        _microsoft.Time.Advance(TimeSpan.FromMinutes(61));

        await PostAsync(Activities.Consent(fileId, "i2", uploadInfo: UploadInfo()));

        Assert.Contains("no longer", await TextAsync(2), StringComparison.Ordinal);
        Assert.Single(Puts);
    }

    private static (OutboundFile File, TaskCompletionSource Go, TaskCompletionSource Opened) Held(string content)
    {
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return (
            new OutboundFile
            {
                Name = "slow.txt",
                Size = content.Length,
                OpenAsync = async ct =>
                {
                    opened.TrySetResult();
                    await go.Task.WaitAsync(ct);
                    return new MemoryStream(Encoding.UTF8.GetBytes(content));
                },
            },
            go,
            opened);
    }

    private static Dictionary<string, object?> ConsentOf(string user, string fileId, string id)
    {
        var activity = Activities.Consent(fileId, id, uploadInfo: UploadInfo());
        activity["from"] = new { id = "29:" + user, aadObjectId = user };
        return activity;
    }

    private async Task<string> OfferToAsync(string user, OutboundFile file)
    {
        var activity = Activities.Personal("send it", $"m{Guid.NewGuid():N}");
        activity["from"] = new { id = "29:" + user, aadObjectId = user };
        var message = await NextAsync(activity);
        var index = _microsoft.Replies.Count;
        Assert.Single(await _channel.Responder.SendFilesAsync(message, [file], Ct));
        return Card(await ReplyAsync(index), ConsentType).GetProperty("content").GetProperty("acceptContext").GetProperty("fileId").GetString()!;
    }

    [Fact]
    public async Task One_persons_uploads_wait_for_one_another_and_for_nobody_elses()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.Created, "{}"));
        var held = Enumerable.Range(0, 5).Select(i => Held($"mine{i}")).ToList();
        try
        {
            foreach (var (one, i) in held.Select((h, i) => (h, i)))
                await PostAsync(ConsentOf(Activities.UserId, await OfferToAsync(Activities.UserId, one.File), $"i{i}"));
            await Polling.WaitUntilAsync(() => held.Any(h => h.Opened.Task.IsCompleted), Polling.DefaultTimeout);

            var theirs = await OfferToAsync(OtherUser, Outbound("theirs"));
            var replies = _microsoft.Replies.Count;
            await PostAsync(ConsentOf(OtherUser, theirs, "other"));
            Card(await ReplyAsync(replies), InfoType);

            Assert.Equal("theirs", Assert.Single(Puts).Body);
            Assert.Single(held, h => h.Opened.Task.IsCompleted);
        }
        finally
        {
            held.ForEach(h => h.Go.TrySetResult());
        }

        await Polling.WaitUntilAsync(() => Puts.Count == 6, Polling.DefaultTimeout);
    }

    [Fact]
    public async Task An_offer_being_sent_is_not_sent_twice()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.Created, "{}"));
        var (file, go, opened) = Held("mine");
        var fileId = await OfferAsync(file);
        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));
        await opened.Task.WaitAsync(Polling.DefaultTimeout, Ct);

        await PostAsync(Activities.Consent(fileId, "i2", uploadInfo: UploadInfo()));
        Assert.Contains("no longer", await TextAsync(1), StringComparison.Ordinal);
        await PostAsync(Activities.Consent(fileId, "i3", "decline"));
        Assert.Contains("no longer", await TextAsync(2), StringComparison.Ordinal);
        go.SetResult();

        Card(await ReplyAsync(3), InfoType);
        Assert.Single(Puts);
    }

    /// <summary>A stream far longer than it was said to be, which counts what is read of it.</summary>
    private sealed class LongStream : MemoryStream
    {
        public long Taken { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Taken += buffer.Length;
            return new(Taken > 2 * TeamsFileExchange.MaxFileBytes ? 0 : buffer.Length);
        }
    }

    [Fact]
    public async Task A_file_that_grew_is_read_to_the_bound_and_not_sent()
    {
        await StartAsync();
        await using var source = new LongStream();
        var fileId = await OfferAsync(new OutboundFile { Name = "a.bin", Size = 5, OpenAsync = _ => Task.FromResult<Stream>(source) });

        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo()));

        Assert.Contains("size changed", await TextAsync(1), StringComparison.Ordinal);
        Assert.InRange(source.Taken, TeamsFileExchange.MaxFileBytes, TeamsFileExchange.MaxFileBytes + 81920);
        Assert.Empty(Puts);
        await PostAsync(Activities.Consent(fileId, "i2", uploadInfo: UploadInfo()));
        Assert.Contains("no longer", await TextAsync(2), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_card_shows_only_an_address_of_the_tenant()
    {
        await StartAsync();
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.Created, "{}"));
        var fileId = await OfferAsync();

        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: new
        {
            uploadUrl = Upload, contentUrl = "https://evil.example/report.txt", uniqueId = "item-1", fileType = "txt", tenantId = FakeBotConnector.TenantId,
        }));

        Assert.Contains("is in your OneDrive", await TextAsync(1), StringComparison.Ordinal);
        Assert.DoesNotContain("evil", _microsoft.Replies[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_whole_host_holds_so_many_requests()
    {
        var consents = new TeamsConsents(TimeProvider.System);
        string? Add(int i) => consents.TryAdd("u", new TeamsReplyTarget(new Uri(FakeBotConnector.ServiceUrl), $"c{i}", "1", "b", "u"), Outbound("x"));

        Assert.All(Enumerable.Range(0, TeamsConsents.Total), i => Assert.NotNull(Add(i)));
        Assert.Null(Add(TeamsConsents.Total));
    }

    [Fact]
    public async Task A_file_gone_or_emptied_since_it_was_offered_is_not_sent()
    {
        await StartAsync();
        var gone = await OfferAsync(new OutboundFile { Name = "gone.txt", Size = 5, OpenAsync = _ => throw new FileNotFoundException("/secret") });
        await PostAsync(Activities.Consent(gone, "i1", uploadInfo: UploadInfo()));
        Assert.Contains("was not sent", await TextAsync(1), StringComparison.Ordinal);

        var emptied = await OfferAsync(Outbound(string.Empty, size: 5));
        await PostAsync(Activities.Consent(emptied, "i2", uploadInfo: UploadInfo()));
        Assert.Contains("was not sent", await TextAsync(3), StringComparison.Ordinal);
        Assert.Empty(Puts);
    }

    [Fact]
    public async Task Without_a_certificate_a_temporary_upload_passes_until_the_date_and_no_card_is_shown_after()
    {
        await StartAsync(certificate: false, now: s_before);
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.OK, "{}"));
        var fileId = await OfferAsync();
        await PostAsync(Activities.Consent(fileId, "i1", uploadInfo: UploadInfo(Upload + "&tempauth=v1.marker")));
        Card(await ReplyAsync(1), InfoType);
        Assert.Null(Assert.Single(Puts).Authorization);

        var message = await MessageAsync();
        _microsoft.Time.Advance(s_after - s_before);
        var result = Assert.Single(await _channel.Responder.SendFilesAsync(message, [Outbound("x")], Ct));
        Assert.Equal(FileSendOutcome.Refused, result.Outcome);
        Assert.Contains("certificate", result.Reason, StringComparison.Ordinal);
        Assert.Equal(2, _microsoft.Replies.Count);
    }

    [Fact]
    public async Task An_empty_file_and_one_too_large_are_refused_before_any_card()
    {
        await StartAsync();
        var message = await MessageAsync();
        var results = await _channel.Responder.SendFilesAsync(message, [Outbound(string.Empty), Outbound("x", size: long.MaxValue), Outbound("y")], Ct);

        Assert.Equal([FileSendOutcome.Refused, FileSendOutcome.TooLarge, FileSendOutcome.AwaitingConsent], results.Select(r => r.Outcome));
        Assert.Contains("empty", results[0].Reason, StringComparison.Ordinal);
        Assert.Single(_microsoft.Replies);
    }

    [Fact]
    public async Task The_requests_held_are_bounded_and_a_card_Teams_refused_holds_none()
    {
        await StartAsync();
        var message = await MessageAsync();
        _microsoft.Connector = () => new FakeHttpResponse(HttpStatusCode.Forbidden, "{}");
        var refused = await _channel.Responder.SendFilesAsync(message, [.. Enumerable.Range(0, 20).Select(_ => Outbound("x"))], Ct);
        Assert.All(refused, r => Assert.Equal(FileSendOutcome.Refused, r.Outcome));

        _microsoft.Connector = () => new FakeHttpResponse(HttpStatusCode.Created, "{}");
        var results = await _channel.Responder.SendFilesAsync(message, [.. Enumerable.Range(0, 11).Select(_ => Outbound("x"))], Ct);
        Assert.Equal(10, results.Count(r => r.Outcome == FileSendOutcome.AwaitingConsent));
        Assert.Contains("10", results[10].Reason, StringComparison.Ordinal);

        _microsoft.Time.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(FileSendOutcome.AwaitingConsent, Assert.Single(await _channel.Responder.SendFilesAsync(message, [Outbound("x")], Ct)).Outcome);
    }

    [Fact]
    public async Task What_get_finds_is_offered_and_the_answer_says_it_waits()
    {
        await StartAsync();
        _answer = _ => new CommandReply("`/outbox/report.txt`", [Outbound("hello"), Outbound(string.Empty, "empty.txt")]);

        await PostAsync(Activities.Personal("/get /outbox/report.txt", "1"));

        Card(await ReplyAsync(0), ConsentType);
        var text = await TextAsync(1);
        Assert.Contains("/outbox/report.txt", text, StringComparison.Ordinal);
        Assert.Contains("accept", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", text, StringComparison.Ordinal);
        Assert.Contains("empty.txt", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_invoke_is_acknowledged_and_does_nothing()
    {
        await StartAsync();
        var fileId = await OfferAsync();
        var activity = Activities.Consent(fileId, "i1", uploadInfo: UploadInfo());
        activity["name"] = "task/fetch";
        Assert.Equal(HttpStatusCode.OK, await PostAsync(activity));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(Activities.Consent(fileId, "i2", "other", UploadInfo())));
        await MessageAsync("sentinel");
        Assert.Single(_microsoft.Replies);
        Assert.Empty(Puts);

        // Neither took the offer.
        _microsoft.Transport.Map(HttpMethod.Put, OneDrive, new FakeHttpResponse(HttpStatusCode.Created, "{}"));
        await PostAsync(Activities.Consent(fileId, "i3", uploadInfo: UploadInfo()));
        Card(await ReplyAsync(1), InfoType);
        Assert.Single(Puts);
    }

    // ---- Settings --------------------------------------------------------------------------

    [Theory]
    [InlineData("contoso.sharepoint.com")]
    [InlineData("contoso-my")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task A_name_that_is_no_SharePoint_name_refuses_the_start(string name)
    {
        await StartAsync();
        var refused = Assert.Throws<HostConfigurationException>((_microsoft.Options() with { SharePointNames = [name] }).Validate);
        Assert.Contains("Teams:SharePointNames", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bot.pem")]
    public async Task A_certificate_variable_that_names_no_absolute_path_refuses_the_start_without_its_value(string? path)
    {
        await StartAsync();
        var variable = $"ORKEON_TEST_CERT_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, path);
        try
        {
            var refused = Assert.Throws<HostConfigurationException>((_microsoft.Options() with { CertificatePathEnvironmentVariable = variable }).Validate);
            Assert.Contains(variable, refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("bot.pem", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}
