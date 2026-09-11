namespace Orkeon.Constants.Llm;

/// <summary>
/// The OpenTelemetry semantic conventions for generative AI, as attribute names.
/// <para>
/// A span that carries these names is understood by every backend that reads the
/// convention -- Langfuse, Honeycomb, Application Insights, the Aspire dashboard -- with
/// no mapping on their side: the model shows up as the model, the token counts as
/// token counts. Orkeon-specific facts (task id, crew name, estimated cost) keep the
/// <c>orkeon.*</c> prefix; nothing here duplicates them.
/// </para>
/// <para>
/// Names follow the convention at its 1.37 state (<c>gen_ai.provider.name</c> replaced
/// <c>gen_ai.system</c>). They are shared by the execution loop (Application), the tracing
/// helpers (Infrastructure) and the scripting runtime, which is why they live in a
/// zero-dependency satellite (ADR-009) rather than in any one of them.
/// </para>
/// </summary>
public static class GenAiAttributes
{
    /// <summary>The operation: <see cref="OperationChat"/>, <see cref="OperationInvokeAgent"/>, <see cref="OperationExecuteTool"/>.</summary>
    public const string OperationName = "gen_ai.operation.name";

    /// <summary>The provider that served the call, in the convention's lower-case vocabulary (<c>openai</c>, <c>anthropic</c>, <c>ollama</c>...).</summary>
    public const string ProviderName = "gen_ai.provider.name";

    /// <summary>The model the request asked for.</summary>
    public const string RequestModel = "gen_ai.request.model";

    /// <summary>The model the response was produced by, when the provider reports it.</summary>
    public const string ResponseModel = "gen_ai.response.model";

    /// <summary>The identifier the provider gave the response.</summary>
    public const string ResponseId = "gen_ai.response.id";

    /// <summary>Why generation stopped: <c>stop</c>, <c>tool_calls</c>, <c>length</c>, <c>content_filter</c>.</summary>
    public const string ResponseFinishReasons = "gen_ai.response.finish_reasons";

    /// <summary>Tokens in the prompt.</summary>
    public const string UsageInputTokens = "gen_ai.usage.input_tokens";

    /// <summary>Tokens in the completion.</summary>
    public const string UsageOutputTokens = "gen_ai.usage.output_tokens";

    /// <summary>The temperature the request asked for.</summary>
    public const string RequestTemperature = "gen_ai.request.temperature";

    /// <summary>The maximum number of tokens the request allowed.</summary>
    public const string RequestMaxTokens = "gen_ai.request.max_tokens";

    /// <summary>The name of the agent an <see cref="OperationInvokeAgent"/> span runs, or that a nested span runs for.</summary>
    public const string AgentName = "gen_ai.agent.name";

    /// <summary>The identifier of the agent.</summary>
    public const string AgentId = "gen_ai.agent.id";

    /// <summary>The name of the tool an <see cref="OperationExecuteTool"/> span executes.</summary>
    public const string ToolName = "gen_ai.tool.name";

    /// <summary>The identifier the model gave the tool call, so the span pairs with the <c>tool_calls</c> entry.</summary>
    public const string ToolCallId = "gen_ai.tool.call.id";

    /// <summary>The conversation (run) every span of one crew execution belongs to.</summary>
    public const string ConversationId = "gen_ai.conversation.id";

    /// <summary>The class of error that ended the operation, when it failed (<c>error.type</c> of the general conventions).</summary>
    public const string ErrorType = "error.type";

    /// <summary>The value of <see cref="OperationName"/> for a chat completion, and the prefix of its span name (<c>chat {model}</c>).</summary>
    public const string OperationChat = "chat";

    /// <summary>The value of <see cref="OperationName"/> for an agent turn, and the prefix of its span name (<c>invoke_agent {agent}</c>).</summary>
    public const string OperationInvokeAgent = "invoke_agent";

    /// <summary>The value of <see cref="OperationName"/> for a tool execution, and the prefix of its span name (<c>execute_tool {tool}</c>).</summary>
    public const string OperationExecuteTool = "execute_tool";

    /// <summary>The <c>gen_ai.client.token.usage</c> histogram (tokens), with a <see cref="TokenType"/> attribute.</summary>
    public const string MetricClientTokenUsage = "gen_ai.client.token.usage";

    /// <summary>The <c>gen_ai.client.operation.duration</c> histogram (seconds).</summary>
    public const string MetricClientOperationDuration = "gen_ai.client.operation.duration";

    /// <summary>The attribute that splits <see cref="MetricClientTokenUsage"/>: <c>input</c> or <c>output</c>.</summary>
    public const string TokenType = "gen_ai.token.type";

    /// <summary>The span name of an operation on a model: <c>{operation} {model}</c>.</summary>
    public static string SpanName(string operation, string? target) =>
        string.IsNullOrEmpty(target) ? operation : operation + " " + target;
}
