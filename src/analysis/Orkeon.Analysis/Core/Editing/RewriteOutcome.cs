using Orkeon.Analysis.Core.Structural;

namespace Orkeon.Analysis.Core.Editing;

internal sealed record SkippedMatch(StructuralMatch Match, string Reason);

internal sealed record RewriteOutcome(byte[] Bytes, int Applied, IReadOnlyList<SkippedMatch> Skipped);
