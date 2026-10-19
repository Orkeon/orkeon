namespace Orkeon.Constants.Cli;

/// <summary>
/// Keys of the REPL's session state (<c>ISessionBufferService.GetState</c>/<c>SetState</c>, which a
/// scripted command reaches through the <c>session_store</c> tool's <c>get_state</c>/<c>set_state</c>)
/// that the host itself reads or writes. Each is a contract between the console and the commands
/// of a product built on it: a key renamed on one side and not the other breaks the feature with
/// no error, so both sides name it from here, or from its documentation.
/// </summary>
public static class SessionStateKeys
{
    /// <summary>
    /// The session's permission mode (<c>default</c>, <c>acceptEdits</c>, <c>plan</c>,
    /// <c>bypassPermissions</c>, <c>dontAsk</c>): written by a product's <c>/mode</c> command, shown by
    /// the console and cycled on Shift+Tab.
    /// </summary>
    public const string DefaultPermissionMode = "default_permission_mode";

    /// <summary>The model selected for the session; shown in the hint bar.</summary>
    public const string SelectedModel = "selected_model";
}
