namespace Orkeon.Domain.Constants.Task;

/// <summary>
/// Length limits of a task's text value objects: its description and its expected output.
/// </summary>
public static class TaskDefaults
{
    /// <summary>Maximum length of <see cref="Orkeon.Domain.Task.ValueObjects.TaskDescription"/> (2¹⁵).</summary>
    public const int TaskDescriptionMaxLength = 32_768;

    /// <summary>Maximum length of <see cref="Orkeon.Domain.Task.ValueObjects.ExpectedOutput"/> (2¹²).</summary>
    public const int ExpectedOutputMaxLength = 4_096;
}
