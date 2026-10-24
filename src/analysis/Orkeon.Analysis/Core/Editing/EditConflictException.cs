#pragma warning disable CA1032, CA1064
namespace Orkeon.Analysis.Core.Editing;

internal sealed class EditConflictException(string message) : Exception(message);
