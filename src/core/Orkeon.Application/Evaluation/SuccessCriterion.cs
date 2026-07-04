using System.Globalization;

namespace Orkeon.Application.Evaluation;

/// <summary>
/// Defines success criteria for training tasks and evaluations.
/// </summary>
public record SuccessCriterion(
    string Name,
    string Description,
    Func<object, bool> Evaluator,
    double Weight = 1.0)
{
    /// <summary>
    /// Evaluates whether the criterion is met.
    /// </summary>
    public bool IsMet(object result) => Evaluator(result);

    /// <summary>
    /// Creates a threshold-based criterion.
    /// </summary>
    public static SuccessCriterion Threshold(
        string name,
        string description,
        double threshold,
        Func<object, double> valueExtractor)
    {
        return new SuccessCriterion(
            name,
            description,
            result => valueExtractor(result) >= threshold);
    }

    /// <summary>
    /// Creates a boolean criterion.
    /// </summary>
    public static SuccessCriterion Boolean(
        string name,
        string description,
        Func<object, bool> condition)
    {
        return new SuccessCriterion(name, description, condition);
    }

    /// <summary>
    /// To String.
    /// </summary>
    public override string ToString()
    {
        return $"SuccessCriterion {{ Name = {Name}, Description = {Description}, Evaluator = {Evaluator}, Weight = {Weight.ToString(CultureInfo.InvariantCulture)} }}";
    }
}
