using Orkeon.Studio.Core.Validation;

using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Common;

/// <summary>
/// A single <see cref="ValidationMessage"/> ready for display: the Core record plus the strings and
/// flags the view binds to (icon glyph, ordering weight, path prefix).
/// </summary>
public sealed class ValidationMessageViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Wraps a Core validation message.</summary>
    public ValidationMessageViewModel(ValidationMessage message, IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        Message = message;
        _strings = strings ?? EnglishStudioStrings.Instance;
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

    /// <summary>
    /// The plain-language reading of the finding (T-08): resolved per <see cref="Code"/>
    /// from the localization port, falling back to <see cref="Display"/> for a code with
    /// no overlay. The raw CLI-grade line stays available for the expert tooltip — the
    /// stable code and the English remediation never leave the screen, they step back.
    /// </summary>
    public string FriendlyText
    {
        get
        {
            var key = FriendlyKeyPrefix + Code;
            var localized = _strings[key];
            return localized == key ? Display : localized;
        }
    }

    /// <summary>Resx key prefix of the per-code overlay, e.g. <c>Vm_ValMsg_WIN-01</c>.</summary>
    public const string FriendlyKeyPrefix = "Vm_ValMsg_";

    /// <inheritdoc />
    public override string ToString() => Display;
}
