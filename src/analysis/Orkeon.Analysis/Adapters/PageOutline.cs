using System.Collections.Immutable;

namespace Orkeon.Analysis.Adapters;

/// <summary>What an element of a <see cref="PageOutline"/> is.</summary>
public enum PageElementKind
{
    /// <summary>A heading (<c>h1</c>-<c>h6</c>): it opens a section that lasts until a heading of its level or above.</summary>
    Heading,

    /// <summary>An element that holds others by nesting: a landmark (navigation, main...), a form, a dialog.</summary>
    Region,

    /// <summary>A hyperlink.</summary>
    Link,

    /// <summary>An interactive element: a button, a text box, a check box, a list.</summary>
    Control,
}

/// <summary>One element of a web page, as a browser reports it.</summary>
public sealed record PageElement
{
    /// <summary>What the element is.</summary>
    public required PageElementKind Kind { get; init; }

    /// <summary>Accessibility role (<c>heading</c>, <c>navigation</c>, <c>form</c>, <c>link</c>, <c>button</c>, <c>textbox</c>...).</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Accessible name: the text a user reads or hears for the element.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Level of a heading, 1 to 6; ignored for the other kinds.</summary>
    public int Level { get; init; }

    /// <summary>
    /// Index, in <see cref="PageOutline.Elements"/>, of the closest <see cref="PageElementKind.Region"/> that
    /// encloses the element; <c>-1</c> when none does.
    /// </summary>
    public int Container { get; init; } = -1;

    /// <summary>Text of the element; for a heading, the text of its section up to the next heading.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Target of a link, as the page writes it.</summary>
    public string? Target { get; init; }

    /// <summary>The <c>id</c> attribute of the element, which an in-page link (<c>#id</c>) points at.</summary>
    public string? Anchor { get; init; }

    /// <summary><c>false</c> when the page does not render the element.</summary>
    public bool Visible { get; init; } = true;
}

/// <summary>
/// The outline of one web page: its headings, regions, links and controls in document order. Input of
/// <see cref="PageOutlineMapper"/>.
/// </summary>
public sealed record PageOutline
{
    /// <summary>
    /// Virtual path every node of the page carries (its <c>VirtualFilePath</c>) and from which its ids are
    /// built: distinct for each outline a caller keeps apart.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>Address of the page.</summary>
    public required string Location { get; init; }

    /// <summary>Title of the page.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The text of the page no heading holds: what comes before the first one. Kept by the root node.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The elements of the page, in document order.</summary>
    public required ImmutableList<PageElement> Elements { get; init; }
}
