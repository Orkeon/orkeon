using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Builds Gemini-shaped OpenAI-dialect chat-completion bodies for tool-call replay tests.
/// </summary>
public static class GeminiToolCallBodies
{
    /// <summary>A buffered answer whose calls each carry <c>extra_content.google.thought_signature</c>.</summary>
    public static string Buffered(params (string id, string signature)[] calls) =>
        Body(calls.Select(c => (object)new
        {
            id = c.id,
            type = "function",
            function = new { name = "directory_read", arguments = "{\"path\":\"/src\"}" },
            extra_content = new { google = new { thought_signature = c.signature } },
        }).ToArray());

    /// <summary>A buffered answer whose single call carries only id, type and function.</summary>
    public static string BufferedWithoutExtras(string id) =>
        Body([new
        {
            id,
            type = "function",
            function = new { name = "directory_read", arguments = "{\"path\":\"/src\"}" },
        }]);

    /// <summary>A buffered answer whose calls may share an id yet differ by function name and arguments.</summary>
    public static string BufferedDistinct(params (string id, string name, string arguments, string signature)[] calls) =>
        Body(calls.Select(c => (object)new
        {
            id = c.id,
            type = "function",
            function = new { name = c.name, arguments = c.arguments },
            extra_content = new { google = new { thought_signature = c.signature } },
        }).ToArray());

    /// <summary>A plain final text answer.</summary>
    public static string FinalText(string text = "done") =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content = text }, finish_reason = "stop" },
            },
            usage = new { prompt_tokens = 5, completion_tokens = 2, total_tokens = 7 },
        });

    /// <summary>
    /// The SSE text of a Gemini-shaped OpenAI-dialect stream: per call index, three <c>delta.tool_calls</c>
    /// fragments (id/type/name, then the arguments in two pieces); <c>extra_content</c> rides the fragment
    /// numbered <c>fragmentCarryingIt</c> of that index.
    /// </summary>
    public static string StreamedFragments(params (string id, string signature, int fragmentCarryingIt)[] calls)
    {
        var sb = new StringBuilder();
        for (var index = 0; index < calls.Length; index++)
        {
            var (id, signature, carrying) = calls[index];
            string[] args = ["", "{\"path\"", ":\"/src\"}"];
            for (var f = 0; f < args.Length; f++)
            {
                var tc = new JsonObject { ["index"] = index };
                if (f == 0)
                {
                    tc["id"] = id;
                    tc["type"] = "function";
                    tc["function"] = new JsonObject { ["name"] = "directory_read", ["arguments"] = args[f] };
                }
                else
                {
                    tc["function"] = new JsonObject { ["arguments"] = args[f] };
                }

                if (f == carrying)
                {
                    tc["extra_content"] = new JsonObject
                    {
                        ["google"] = new JsonObject { ["thought_signature"] = signature }
                    };
                }

                AppendChunk(sb, new JsonObject
                {
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = new JsonObject { ["tool_calls"] = new JsonArray(tc) }
                    })
                });
            }
        }

        AppendChunk(sb, new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["delta"] = new JsonObject(),
                ["finish_reason"] = "tool_calls"
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 5, ["completion_tokens"] = 2, ["total_tokens"] = 7 }
        });
        sb.Append("data: [DONE]\n\n");
        return sb.ToString();
    }

    private static void AppendChunk(StringBuilder sb, JsonObject chunk) =>
        sb.Append("data: ").Append(chunk.ToJsonString()).Append("\n\n");

    private static string Body(object[] toolCalls) =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { role = "assistant", content = (string?)null, tool_calls = toolCalls },
                    finish_reason = "tool_calls",
                },
            },
            usage = new { prompt_tokens = 5, completion_tokens = 2, total_tokens = 7 },
        });
}
