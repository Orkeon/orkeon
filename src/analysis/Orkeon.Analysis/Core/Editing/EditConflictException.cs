#pragma warning disable CA1032, CA1064

namespace Orkeon.Analysis.Core.Editing;

public sealed class EditConflictException : Exception
{
    public EditConflictException(string message)
        : base(message)
    {
    }
}
