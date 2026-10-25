using System.Text;
using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core.Editing.Intent;

internal enum EnsureMemberKind { Member, Field }

/// <summary>
/// Adds a field, property or method to a type body unless a member of that name and kind is already there, and
/// places it with its peers: after the last peer of its kind, else a field first, a property after the fields, a method last.
/// </summary>
internal sealed partial class EnsureMemberOperation(NodeSelector type, string text, EnsureMemberKind kind = EnsureMemberKind.Member) : IEditOperation
{
    private enum Category { Field, Property, Method }

    [GeneratedRegex(@"^(\s*(\[[^\]]*\]|@[\w.]+(\([^)]*\))?))*\s*")]
    private static partial Regex LeadingDecorations();

    // A Go method's receiver comes before its name: func (t *T) Name(...).
    [GeneratedRegex(@"^\s*func\s*\([^)]*\)\s*")]
    private static partial Regex GoReceiver();

    [GeneratedRegex(@"<[^<>()]*>\s*$")]
    private static partial Regex TrailingGenerics();

    [GeneratedRegex(@"[A-Za-z_$][\w$]*")]
    private static partial Regex Identifier();

    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        var declaration = parsed.Resolve(type);
        var members = parsed.BodyMembers(declaration);
        if (members.Count == 0) return new NodeInsertOperation(type, InsertPosition.Inside, text).Apply(current, parsed);

        var typescript = parsed.Language is "typescript" or "tsx";
        var category = kind == EnsureMemberKind.Field ? Category.Field : Classify(text);
        var name = NameOf(text, category, typescript)
            ?? throw new ArgumentException("The member text has no recognisable name.");
        var existing = members.FirstOrDefault(m => CategoryOf(m.Kind) == category && NameOf(m.Text, category, typescript) == name);
        if (existing is not null)
            return new EditResult { Bytes = current.ToArray(), Range = existing.Range, Applied = false };

        var file = current.Span;
        var eol = IndentationDetector.LineEnding(file);
        var lines = text.TrimEnd('\r', '\n').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var peer = members.LastOrDefault(m => CategoryOf(m.Kind) == category);
        var gap = category == Category.Method ? eol + eol : eol;
        if (peer is null && category == Category.Property) peer = members.LastOrDefault(m => CategoryOf(m.Kind) == Category.Field);
        if (peer is null && category == Category.Method) peer = members[^1];

        int at;
        string indent;
        string lead;
        string tail;
        if (peer is not null)
        {
            at = peer.Range.EndByte;
            indent = IndentationDetector.IndentOf(file, peer.Range.StartByte);
            lead = gap + indent;
            tail = string.Empty;
        }
        else
        {
            at = members[0].Range.StartByte;
            indent = IndentationDetector.IndentOf(file, at);
            lead = string.Empty;
            tail = eol + eol + indent;
        }

        var block = string.Join(eol, lines.Select((l, i) => i == 0 || l.Length == 0 ? l : indent + l));
        var insertion = Encoding.UTF8.GetBytes(lead + block + tail);
        var blockStart = at + Encoding.UTF8.GetByteCount(lead);
        var bytes = new byte[current.Length + insertion.Length];
        file[..at].CopyTo(bytes);
        insertion.CopyTo(bytes.AsSpan(at));
        file[at..].CopyTo(bytes.AsSpan(at + insertion.Length));
        var startLine = bytes.AsSpan(0, blockStart).Count((byte)'\n') + 1;
        return new EditResult
        {
            Bytes = bytes,
            Range = new NodeRange(blockStart, blockStart + Encoding.UTF8.GetByteCount(block), startLine, startLine + lines.Length - 1),
        };
    }

    private static Category CategoryOf(string nodeKind) => nodeKind switch
    {
        "field_declaration" or "public_field_definition" => Category.Field,
        "property_declaration" or "property_signature" => Category.Property,
        _ => Category.Method,
    };

    private static Category Classify(string member)
    {
        var body = LeadingDecorations().Replace(member, string.Empty);
        var paren = body.IndexOf('(', StringComparison.Ordinal);
        var stop = body.IndexOfAny(['{', '=', ';']);
        if (paren >= 0 && (stop < 0 || paren < stop)) return Category.Method;
        return stop >= 0 && (body[stop] == '{' || body.AsSpan(stop).StartsWith("=>", StringComparison.Ordinal))
            ? Category.Property
            : Category.Field;
    }

    private static string? NameOf(string member, Category category, bool typescript)
    {
        var head = GoReceiver().Replace(LeadingDecorations().Replace(member, string.Empty), string.Empty);
        var cut = category == Category.Method ? head.IndexOf('(', StringComparison.Ordinal) : head.IndexOfAny(['{', '=', ';']);
        if (cut >= 0) head = head[..cut];
        if (category == Category.Method) head = TrailingGenerics().Replace(head, string.Empty);
        else if (typescript && head.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0) head = head[..colon];
        var names = Identifier().Matches(head);
        return names.Count == 0 ? null : names[^1].Value;
    }
}
