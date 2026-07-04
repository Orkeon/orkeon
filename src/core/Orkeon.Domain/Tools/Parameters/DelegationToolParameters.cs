namespace Orkeon.Domain.Tools.Parameters;

/// <summary>Parameters for delegating work to a coworker agent.</summary>
/// <param name="Task">The task description to delegate.</param>
/// <param name="Context">Contextual information to help the coworker complete the task.</param>
/// <param name="CoworkerRole">The role of the coworker to delegate the task to.</param>
public record DelegateWorkParameters(
    string Task,
    string Context,
    string CoworkerRole
);

/// <summary>Parameters for asking a question to a coworker agent.</summary>
/// <param name="Question">The question to ask the coworker.</param>
/// <param name="Context">Contextual information relevant to the question.</param>
/// <param name="CoworkerRole">The role of the coworker to direct the question to.</param>
public record AskQuestionParameters(
    string Question,
    string Context,
    string CoworkerRole
);
