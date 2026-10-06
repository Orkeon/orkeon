namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>What the backends share about the opaque cursors of <c>email_search</c>.</summary>
internal static class Cursors
{
    /// <summary>
    /// The refusal of a cursor the backend cannot read. It says why — the agent loop forwards
    /// nothing else — and the two ways out: the previous page's cursor, or no cursor at all.
    /// </summary>
    /// <param name="cursor">What the call passed.</param>
    /// <param name="form">How a cursor of this backend reads, e.g. <c>u:&lt;number&gt;</c>.</param>
    public static EmailToolException Refused(string cursor, string form) =>
        new(EmailErrorCode.InvalidRequest,
            $"`cursor` '{Previews.Shorten(cursor)}' is not a cursor this account issued (its cursors read `{form}`): it was altered, or it comes from another account. "
            + "Pass `next_cursor` exactly as the previous page returned it, with the same folder and criteria, or leave `cursor` out to start again from the newest message.");
}
