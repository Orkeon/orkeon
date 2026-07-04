using System.Globalization;

namespace Orkeon.Application.Training;

/// <summary>
/// Represents skill development progress for an agent.
/// </summary>
public record SkillDevelopment(
    string SkillName,
    double CurrentLevel,
    double TargetLevel,
    double ImprovementRate,
    int PracticeHours,
    IReadOnlyList<string> CompletedExercises)
{
    /// <summary>
    /// Gets the progress percentage towards the target level.
    /// </summary>
    public double ProgressPercentage =>
        TargetLevel > 0 ? Math.Min((CurrentLevel / TargetLevel) * 100, 100) : 0;

    /// <summary>
    /// Checks if the skill target has been achieved.
    /// </summary>
    public bool IsTargetAchieved => CurrentLevel >= TargetLevel;

    /// <summary>
    /// To String.
    /// </summary>
    public override string ToString()
    {
        return $"SkillDevelopment {{ SkillName = {SkillName}, CurrentLevel = {CurrentLevel.ToString(CultureInfo.InvariantCulture)}, TargetLevel = {TargetLevel.ToString(CultureInfo.InvariantCulture)}, ImprovementRate = {ImprovementRate.ToString(CultureInfo.InvariantCulture)}, PracticeHours = {PracticeHours}, CompletedExercises = {CompletedExercises} }}";
    }
}
