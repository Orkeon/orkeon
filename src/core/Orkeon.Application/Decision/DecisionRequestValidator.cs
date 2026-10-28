using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Validation;
using Orkeon.Constants.Llm;

namespace Orkeon.Application.Decision;

/// <summary>
/// Validates a <see cref="DecisionRequest"/> before it leaves the process, with the limits the
/// decision API documents, so a malformed question fails here with its name rather than as a
/// 422 from the provider.
/// </summary>
public sealed class DecisionRequestValidator : ICommandValidator<DecisionRequest>
{
    /// <inheritdoc />
    public ValidationResult Validate(DecisionRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<string>();

        if (command.Questions.IsEmpty)
            errors.Add("A decision request needs at least one question.");

        foreach (var (name, question) in command.Questions)
        {
            if (string.IsNullOrWhiteSpace(name))
                errors.Add("A question name must not be blank.");

            switch (question)
            {
                case ChoiceQuestion choice when choice.Criteria.Count is 0 or > DecisionLimits.MaxChoiceOptions:
                    errors.Add($"Choice question '{name}' needs 1 to {DecisionLimits.MaxChoiceOptions} options, not {choice.Criteria.Count}.");
                    break;
                case ScoreQuestion score when score.Criteria.Count is 0 or > DecisionLimits.MaxScoreLevels:
                    errors.Add($"Score question '{name}' needs 1 to {DecisionLimits.MaxScoreLevels} levels, not {score.Criteria.Count}.");
                    break;
                case BoxQuestion when command.Assets is not { Count: > 0 }:
                    errors.Add($"Box question '{name}' needs an image among the request's assets.");
                    break;
            }
        }

        foreach (var (placeholder, image) in command.Assets ?? [])
        {
            if (string.IsNullOrWhiteSpace(placeholder))
                errors.Add("An image placeholder must not be blank.");
            if (image.Format is not ("png" or "jpg" or "jpeg"))
                errors.Add($"Image '{placeholder}' has format '{image.Format}'; png, jpg or jpeg expected.");
            if (string.IsNullOrEmpty(image.DataBase64))
                errors.Add($"Image '{placeholder}' carries no data.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}
