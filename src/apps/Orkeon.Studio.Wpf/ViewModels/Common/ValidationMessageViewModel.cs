using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Wpf.ViewModels.Common;

/// <summary>
/// A single <see cref="ValidationMessage"/> ready for display: the Core record plus the strings and
/// flags the view binds to (icon glyph, ordering weight, path prefix).
/// </summary>
public sealed class ValidationMessageViewModel
{
    /// <summary>Wraps a Core validation message.</summary>
    public ValidationMessageViewModel(ValidationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
    }

    /// <summary>The underlying Core message.</summary>
    public ValidationMessage Message { get; }

    /// <summary>How serious the finding is.</summary>
    public ValidationSeverity Severity => Message.Severity;

    /// <summary>The stable diagnostic code, e.g. <c>WIN-01</c>.</summary>
    public string Code => Message.Code;

    /// <summary>The human-readable explanation.</summary>
    public string Text => Message.Text;

    /// <summary>The configuration path or mount string the message is about, when there is one.</summary>
    public string? Path => Message.Path;

    /// <summary>Whether the message must block a save or a launch.</summary>
    public bool IsError => Severity == ValidationSeverity.Error;

    /// <summary>A short severity glyph, so the view needs no converter.</summary>
    public string Glyph => Severity switch
    {
        ValidationSeverity.Error => "✖",       // heavy multiplication X
        ValidationSeverity.Warning => "⚠",     // warning sign
        _ => "ℹ",                              // information source
    };

    /// <summary>
    /// The one-line form shown in the message list, rendered by the shared Core formatter.
    /// The severity is part of the line rather than left to <see cref="Glyph"/> alone: a list
    /// that showed only the code and the text read the same for advice and for a blocking
    /// error, which is the one distinction the reader needs.
    /// </summary>
    public string Display => ValidationMessageFormatter.Format(Message);

    /// <inheritdoc />
    public override string ToString() => Display;
}
