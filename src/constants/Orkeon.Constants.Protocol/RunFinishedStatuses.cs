namespace Orkeon.Constants.Protocol;

/// <summary>
/// The <c>status</c> values a <see cref="RunEventKinds.RunFinished"/> event carries beyond success and failure, and the
/// matching run status of the run history and the host API: one spelling for the runner that writes it and the readers
/// that branch on it.
/// </summary>
public static class RunFinishedStatuses
{
    /// <summary>The run stopped on a question to a human and resumes with its answer (FWK-23).</summary>
    public const string WaitingForInput = "waiting_for_input";
}
