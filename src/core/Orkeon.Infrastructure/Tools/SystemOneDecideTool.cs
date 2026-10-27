using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>The input of <see cref="SystemOneDecideTool"/>.</summary>
public sealed class SystemOneDecideRequest
{
    /// <summary>What is judged: a text, or any JSON value.</summary>
    [FieldSchema(Description = "What is judged: a text, or a JSON value (object, array).", IsRequired = true)]
    [JsonPropertyName("state")]
    public JsonNode? State { get; set; }

    /// <summary>The questions, by name, in the decision model's format.</summary>
    [FieldSchema(
        Description = "The questions, by name. Each is {\"type\":\"choice\",\"criteria\":{\"<option>\":\"<description>\",...}}, " +
                      "{\"type\":\"score\",\"criteria\":[\"<lowest level>\",...,\"<highest level>\"]} or " +
                      "{\"type\":\"noul\",\"criteria\":{\"true\":\"<what yes means>\"}}; an optional \"instructions\" refines one.",
        IsRequired = true)]
    [JsonPropertyName("questions")]
    public JsonNode? Questions { get; set; }

    /// <summary>The model; empty takes the configured one.</summary>
    [FieldSchema(Description = "The decision model; empty takes the configured one.", IsRequired = false)]
    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

/// <summary>The output of <see cref="SystemOneDecideTool"/>.</summary>
public sealed class SystemOneDecideResponse
{
    /// <summary>Whether the decision was taken.</summary>
    [ReturnSchema(Description = "Whether the decision was taken")]
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Each question's typed answer, by name.</summary>
    [ReturnSchema(Description = "Each question's answer by name: choice + confidence + probabilities, score + confidence, or noul (probability of yes)")]
    [JsonPropertyName("answers")]
    public JsonNode? Answers { get; set; }

    /// <summary>The model that answered.</summary>
    [ReturnSchema(Description = "The model that answered")]
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>What went wrong.</summary>
    [ReturnSchema(Description = "What went wrong, when success is false")]
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// <c>system_one_decide</c>: asks the host's decision model (TypeSafe System One) typed questions
/// about a state — a choice among options, a level on a scale, the probability of a yes — and
/// returns calibrated answers. For a judgement an agent wants measured rather than written. The
/// key is the host's; a request never carries one. Images and box questions are not offered.
/// </summary>
[ToolContract("system_one_decide",
    Description = "Ask the decision model typed questions about a state and get calibrated answers: " +
                  "a choice with its probabilities, a score on a scale, or the probability of a yes.")]
public sealed class SystemOneDecideTool : ToolBase<SystemOneDecideRequest, SystemOneDecideResponse>
{
    private static readonly JsonSerializerOptions s_questions = new();

    private readonly IDecisionModel _model;

    /// <summary>Initializes a new instance of <see cref="SystemOneDecideTool"/>.</summary>
    /// <param name="model">The host's decision model.</param>
    public SystemOneDecideTool(IDecisionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(SystemOneDecideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.State is null)
            return "state is required.";
        return request.Questions is JsonObject { Count: > 0 }
            ? null
            : "questions must be an object with at least one question.";
    }

    /// <inheritdoc />
    protected override async Task<SystemOneDecideResponse> ExecuteTypedAsync(SystemOneDecideRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImmutableDictionary<string, DecisionQuestion> questions;
        try
        {
            questions = request.Questions.Deserialize<ImmutableDictionary<string, DecisionQuestion>>(s_questions)
                ?? ImmutableDictionary<string, DecisionQuestion>.Empty;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new SystemOneDecideResponse { Error = $"questions are not readable: {ex.Message}" };
        }

        if (questions.Values.OfType<BoxQuestion>().Any())
            return new SystemOneDecideResponse { Error = "box questions need images, which this tool does not send." };

        try
        {
            var response = await _model.DecideAsync(new DecisionRequest
            {
                Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
                State = request.State!.DeepClone(),
                Questions = questions,
            }, cancellationToken).ConfigureAwait(false);
            return new SystemOneDecideResponse
            {
                Success = true,
                Model = response.Model,
                Answers = JsonSerializer.SerializeToNode(response.Answers),
            };
        }
        catch (DecisionModelException ex)
        {
            return new SystemOneDecideResponse { Error = ex.Message };
        }
    }
}
