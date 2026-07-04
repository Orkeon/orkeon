namespace Orkeon.Application.Execution;

/// <summary>
/// Direct execution context that maintains state across task executions.
/// Used by DirectCrewExecutor for non-Akka execution.
/// </summary>
public class DirectExecutionContext
{
    private DirectExecutionVariables _variables;
    private TaskResultsMap _taskResults = TaskResultsMap.Empty;

    /// <summary>
    /// Initializes a new instance of <see cref="DirectExecutionContext"/>.
    /// </summary>
    public DirectExecutionContext(IReadOnlyDictionary<string, object> variables)
    {
        _variables = DirectExecutionVariables.FromDictionary(variables);
    }

    /// <summary>
    /// Gets or sets the variables.
    /// </summary>
    public IReadOnlyDictionary<string, object> Variables => _variables.ToDictionary();

    /// <summary>
    /// Update From Task Result.
    /// </summary>
    public void UpdateFromTaskResult(string taskId, object result)
    {
        _taskResults = _taskResults.Add(taskId, result);
        _variables = _variables
            .Set($"task_{taskId}_result", result)
            .Set("last_result", result);
    }

    /// <summary>
    /// Get Task Result.
    /// </summary>
    public T? GetTaskResult<T>(string taskId) where T : class
    {
        return _taskResults.Get<T>(taskId);
    }

    /// <summary>
    /// Get Task Result As String.
    /// </summary>
    public string GetTaskResultAsString(string taskId)
    {
        return _taskResults.GetAsString(taskId);
    }

    /// <summary>
    /// Get Variable.
    /// </summary>
    public T? GetVariable<T>(string key) where T : class
    {
        return _variables.Get<T>(key);
    }

    /// <summary>
    /// Set Variable.
    /// </summary>
    public void SetVariable(string key, object value)
    {
        _variables = _variables.Set(key, value);
    }
}
