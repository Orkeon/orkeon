using Jint;
using Jint.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Email.DependencyInjection;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The e-mail family as a script reaches it — the real tools behind <c>tools.email*</c> — held to
/// what <c>Typings/tools.d.ts</c> declares for them.
/// <para>
/// Those declarations were read off the C# DTOs and the binding, and nothing else ties the two
/// languages together: <c>scripts/check-scripting-typings.sh</c> typechecks
/// <c>13-email-triage.ork.ts</c> against them, but that script needs a real mailbox and never
/// runs. <c>email_parser</c> needs neither an account nor a network, so it carries the checks on
/// the result and on integer arguments; the key checks run before any account is resolved, so a
/// family with no account configured is enough for them.
/// </para>
/// </summary>
public sealed class EmailToolTypingsTests
{
    private const string Eml = """
        From: Ada Lovelace <ada@example.com>
        To: team@example.com
        Subject: Quarterly figures
        Date: Fri, 25 Sep 2026 10:00:00 +0000
        Message-ID: <q3@example.com>
        MIME-Version: 1.0
        Content-Type: multipart/mixed; boundary="b1"

        --b1
        Content-Type: text/plain; charset=utf-8

        Please find the figures attached.
        Revenue rose in every region this quarter, led by the north and the coast.
        Costs held flat: the new roaster paid for itself within the first two months.
        Next quarter we open the second shop, so expect a dip in margin before it recovers.
        Questions to Ada before Friday, please; the board meets on Monday morning.
        --b1
        Content-Type: text/csv; name="figures.csv"
        Content-Disposition: attachment; filename="figures.csv"

        q,revenue
        3,42
        --b1--
        """;

    /// <summary>
    /// The result keeps the tool's snake_case keys all the way down, nested objects and arrays
    /// included, and a field the tool leaves null is absent: <c>undefined</c>, which is what the
    /// optional fields of <c>EmailMessage</c> promise, and never <c>null</c>.
    /// </summary>
    [Fact]
    public async Task Email_parser_hands_the_script_the_shape_the_typings_declare()
    {
        await using var services = BuildEmailTools();

        var shape = (await EvaluateAsync(services, """
            tools.emailParser({ path: '/mail/q3.eml' }).then(m => ({
                subject: m.subject,
                from: m.from,
                to: Array.isArray(m.to) ? m.to.join(',') : 'not an array',
                messageId: m.message_id,
                folder: m.folder,
                verdict: m.security.verdict,
                untrusted: m.security.untrusted,
                riskScore: typeof m.security.risk_score,
                text: m.text,
                textOffset: m.text_offset,
                attachment: m.attachments[0].file_name + ' ' + m.attachments[0].content_type,
                absent: [m.account, m.id, m.seen, m.next_offset].every(v => v === undefined),
            }))
            """)).AsObject();

        Assert.Equal("Quarterly figures", shape.Get("subject").AsString());
        Assert.Contains("ada@example.com", shape.Get("from").AsString(), StringComparison.Ordinal);
        Assert.Equal("team@example.com", shape.Get("to").AsString());
        Assert.Equal("q3@example.com", shape.Get("messageId").AsString());
        Assert.Equal("/mail/q3.eml", shape.Get("folder").AsString());
        Assert.Equal("clean", shape.Get("verdict").AsString());
        Assert.True(shape.Get("untrusted").AsBoolean());
        Assert.Equal("number", shape.Get("riskScore").AsString());
        Assert.Contains("Please find the figures attached.", shape.Get("text").AsString(), StringComparison.Ordinal);
        Assert.Equal(0d, shape.Get("textOffset").AsNumber());
        Assert.Equal("figures.csv text/csv", shape.Get("attachment").AsString());
        Assert.True(shape.Get("absent").AsBoolean());
    }

    /// <summary>
    /// The integer parameters (<c>offset</c>, <c>max_chars</c>, <c>limit</c>, <c>index</c>) are
    /// declared <c>number</c>, and a script's number reaches a tool as a double: Jint hands every
    /// JS number over as one, so 200 arrives as 200.0. The argument validator refused that as "not
    /// an integer" — every such call failed before the tool ran — until it took JSON Schema's
    /// definition, a whole number whatever carries it. Both arguments must reach the tool: the
    /// slice starts at <c>offset</c>, is <c>max_chars</c> long, and matches the same window of the
    /// whole body.
    /// </summary>
    [Fact]
    public async Task An_integer_argument_from_a_script_reaches_the_tool()
    {
        await using var services = BuildEmailTools();

        var slice = (await EvaluateAsync(services, """
            tools.emailParser({ path: '/mail/q3.eml' }).then(whole =>
                tools.emailParser({ path: '/mail/q3.eml', offset: 5, max_chars: 200 }).then(part => ({
                    offset: part.text_offset,
                    length: part.text.length,
                    next: part.next_offset,
                    sameWindow: part.text === whole.text.substring(5, 205),
                })))
            """)).AsObject();

        Assert.Equal(5d, slice.Get("offset").AsNumber());
        Assert.Equal(200d, slice.Get("length").AsNumber());
        Assert.Equal(205d, slice.Get("next").AsNumber());
        Assert.True(slice.Get("sameWindow").AsBoolean());
    }

    /// <summary>
    /// Why the declarations spell every key the tool's way. The required-parameter check looks a
    /// key up by its exact snake_case name before anything converts the object, so the camelCase
    /// spelling a TypeScript author reaches for first is refused as missing, while the declared
    /// spelling passes every argument check and only then meets the missing account.
    /// </summary>
    [Fact]
    public async Task A_required_argument_is_found_by_its_snake_case_name_only()
    {
        await using var services = BuildEmailTools();

        var camel = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync(services, "tools.emailRenameFolder({ path: 'Clients', newName: 'Customers' })"));
        var snake = await Assert.ThrowsAnyAsync<Exception>(() =>
            EvaluateAsync(services, "tools.emailRenameFolder({ path: 'Clients', new_name: 'Customers' })"));

        Assert.Contains("Required parameter 'new_name' is missing", camel.ToString(), StringComparison.Ordinal);
        Assert.Contains("No e-mail account is configured", snake.ToString(), StringComparison.Ordinal);
    }

    private static ServiceProvider BuildEmailTools()
    {
        var fileSystem = new FakeFileSystemService()
            .AddMount("/mail")
            .AddFile("/mail/q3.eml", Eml.ReplaceLineEndings("\r\n"));
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(fileSystem);
        services.AddOrkeonEmailTools(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static Task<JsValue> EvaluateAsync(ServiceProvider services, string script)
    {
        var engine = new JsEngineFactory(builtInTools: services.GetServices<IBaseTool>().ToArray()).Create();
        return Task.Run(() => engine.Evaluate(script).UnwrapIfPromise());
    }
}
