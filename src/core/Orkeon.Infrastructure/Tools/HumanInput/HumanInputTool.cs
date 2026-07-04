using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Constants.HumanInput;
using Orkeon.Domain.HumanInput;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools.HumanInput;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Request parameters for a human input prompt.</summary>
public record HumanInputRequest
{
    /// <summary>Gets the prompt shown to the user.</summary>
    [FieldSchema(Description = "The prompt text shown to the user", Example = "Approve the generated CR?")]
    public string Prompt { get; init; } = "";

    /// <summary>Gets the input type. One of text|approval|choice.</summary>
    [FieldSchema(Description = "Input type: text (free text), approval (yes/no), or choice (one of options[])", IsRequired = false, Example = "approval")]
    public string InputType { get; init; } = "text";

    /// <summary>Gets the allowed choices when InputType=choice.</summary>
    [FieldSchema(Description = "Allowed choices when input_type=choice", IsRequired = false)]
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>Gets the default value if the user provides no answer.</summary>
    [FieldSchema(Description = "Default value returned if the user gives no answer", IsRequired = false, Example = "approved")]
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Virtual path of a file the user may edit before answering. Capable
    /// providers (e.g. the Terminal.Gui modal) open an inline editor when the
    /// user selects a choice whose value contains the substring <c>edit</c>
    /// (case-insensitive, typically <c>edit_then_approve</c>). Providers that
    /// cannot edit (auto-approve, plain console) ignore this field.
    /// </summary>
    [FieldSchema(Description = "Optional virtual path the user may edit before approving. Capable providers open an inline editor when a choice whose value contains 'edit' is selected. Path semantics are owned by the caller; the provider routes I/O through IFileSystemService.", IsRequired = false, Example = "/output/report.md")]
    public string? EditFilePath { get; init; }
}

/// <summary>Response from a human input prompt.</summary>
public record HumanInputResponse
{
    /// <summary>Gets the raw response string from the user.</summary>
    [ReturnSchema(Description = "Raw response string", Example = "approved")]
    public string Response { get; init; } = "";

    /// <summary>Gets the input type that produced the response.</summary>
    [ReturnSchema(Description = "Echoed input type", Example = "approval")]
    public string InputType { get; init; } = "";

    /// <summary>Gets whether the response signals approval (only meaningful for approval/choice types).</summary>
    [ReturnSchema(Description = "True when the response means approval (approval/choice types only)", Example = true)]
    public bool IsApproved { get; init; }

    /// <summary>Gets whether the default value was used (no user input).</summary>
    [ReturnSchema(Description = "True when the default value was used because the user gave no answer", Example = false)]
    public bool WasDefault { get; init; }
}

/// <summary>
/// Built-in tool exposing <see cref="IHumanInputProvider"/> to agents.
/// Auto-injected by the orchestrator when <c>task.HumanInput == true</c>.
/// </summary>
[ToolContract("human_input",
    Name = "human_input",
    Description = "Ask the human user for input (text, approval, or a choice). The runtime suspends until the user responds.",
    Category = "Interaction")]
public partial class HumanInputTool : ToolBase<HumanInputRequest, HumanInputResponse>
{
    private static readonly HashSet<string> ApprovedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "y", "yes", "approve", "approved", "ok", "true", "edit_then_approve",
    };

    private readonly IHumanInputProvider _provider;

    /// <summary>Initializes a new instance of <see cref="HumanInputTool"/>.</summary>
    /// <param name="provider">The human input provider used to obtain user responses.</param>
    /// <param name="logger">Optional logger.</param>
    public HumanInputTool(
        IHumanInputProvider provider,
        ILogger<HumanInputTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(HumanInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return "Prompt cannot be empty";

        var normalized = NormalizeType(request.InputType);
        if (normalized is null)
            return $"Unknown input_type '{request.InputType}'. Expected one of: text, approval, choice.";

        if (normalized == HumanInputDefaults.ChoiceInputType && request.Choices.Count == 0)
            return "input_type=choice requires a non-empty choices[] list";

        return null;
    }

    /// <inheritdoc />
    protected override Task<HumanInputResponse> ExecuteTypedAsync(
        HumanInputRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<HumanInputResponse> ExecuteTypedCoreAsync()
        {
            var inputType = NormalizeType(request.InputType) ?? HumanInputDefaults.TextInputType;
            var metadata = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(request.EditFilePath))
            {
                metadata[HumanInputDefaults.EditFilePathMetadataKey] = request.EditFilePath.Trim();
            }
            var context = new HumanInputContext
            {
                Prompt = request.Prompt,
                InputType = inputType,
                DefaultValue = request.DefaultValue,
                Options = inputType == HumanInputDefaults.ChoiceInputType
                    ? request.Choices
                    : Array.Empty<string>(),
                Metadata = metadata,
            };

            LogPromptingHuman(inputType, request.Prompt.Length);

            string raw;
            bool isApproved;
            switch (inputType)
            {
                case HumanInputDefaults.ConfirmationInputType:
                {
                    var confirmed = await _provider.GetConfirmationAsync(context, cancellationToken).ConfigureAwait(false);
                    raw = confirmed ? "approved" : "rejected";
                    isApproved = confirmed;
                    break;
                }
                case HumanInputDefaults.ChoiceInputType:
                {
                    raw = await _provider.GetChoiceAsync(context, cancellationToken).ConfigureAwait(false);
                    isApproved = IsApprovalToken(raw);
                    break;
                }
                default:
                {
                    raw = await _provider.GetInputAsync(context, cancellationToken).ConfigureAwait(false);
                    isApproved = IsApprovalToken(raw);
                    break;
                }
            }

            var wasDefault = string.Equals(raw, request.DefaultValue, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(request.DefaultValue);

            LogReceivedHumanResponse(inputType, raw.Length, isApproved);

            return new HumanInputResponse
            {
                Response = raw,
                InputType = inputType,
                IsApproved = isApproved,
                WasDefault = wasDefault,
            };
        }
    }

    private static string? NormalizeType(string raw)
    {
#pragma warning disable CA1308 // lowercase is the required wire/storage form, not a comparison normalization
        var t = (raw ?? "").Trim().ToLowerInvariant();
#pragma warning restore CA1308
        return t switch
        {
            "" or "text" => HumanInputDefaults.TextInputType,
            "approval" or "confirmation" or "confirm" or "approve" => HumanInputDefaults.ConfirmationInputType,
            "choice" or "choose" or "select" => HumanInputDefaults.ChoiceInputType,
            _ => null,
        };
    }

    private static bool IsApprovalToken(string? response)
        => !string.IsNullOrWhiteSpace(response) && ApprovedTokens.Contains(response.Trim());

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Prompting human (input_type={InputType}, prompt_len={PromptLen})")]
    private partial void LogPromptingHuman(string inputType, int promptLen);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Received human response (input_type={InputType}, response_len={ResponseLen}, is_approved={IsApproved})")]
    private partial void LogReceivedHumanResponse(string inputType, int responseLen, bool isApproved);
}
