using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Orkeon.Studio.Core.Text;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// A chat bubble's text (STUDIO-57): the little Markdown the assistant writes — bold, italic,
/// inline code, bullet and numbered lists, headings, fenced code — rendered, and selectable,
/// so an answer can be copied out of the bubble. Plain text renders exactly as typed.
/// <para>
/// Two elements share one cell: a read-only <see cref="RichTextBox"/>, which is what the user
/// sees and selects, and a hidden <see cref="TextBlock"/> twin set from the same blocks. The
/// twin is there for the layout alone: a RichTextBox asks for no width of its own and would
/// collapse to a strip inside a left-aligned bubble, while a wrapping TextBlock measures its
/// text — so the twin sizes the cell to the content, and the RichTextBox fills it.
/// </para>
/// Usage: <c>&lt;controls:MarkdownText Source="{Binding Body}" FontSize="13"/&gt;</c>.
/// </summary>
public sealed class MarkdownText : ContentControl
{
    /// <summary>The Markdown (or plain) text to show.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(MarkdownText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((MarkdownText)d).Rebuild()));

    /// <summary>The line height of the running text; NaN leaves the font's own.</summary>
    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
        nameof(LineHeight), typeof(double), typeof(MarkdownText),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((MarkdownText)d).Rebuild()));

    /// <summary>The font of inline and fenced code; null keeps the text font.</summary>
    public static readonly DependencyProperty CodeFontFamilyProperty = DependencyProperty.Register(
        nameof(CodeFontFamily), typeof(FontFamily), typeof(MarkdownText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((MarkdownText)d).Rebuild()));

    private const double ParagraphGap = 6;
    private const double ListIndent = 16;

    private readonly TextBlock _twin = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Hidden,
        IsHitTestVisible = false,
        Focusable = false,
    };

    private readonly RichTextBox _text = new()
    {
        IsReadOnly = true,
        IsReadOnlyCaretVisible = false,
        IsDocumentEnabled = true,
        IsUndoEnabled = false,
        AcceptsReturn = false,
        AcceptsTab = false,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        Margin = new Thickness(0),
        Background = Brushes.Transparent,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Cursor = Cursors.IBeam,
    };

    public MarkdownText()
    {
        Focusable = false;
        IsTabStop = false;
        var cell = new Grid();
        cell.Children.Add(_twin);
        cell.Children.Add(_text);
        Content = cell;
        Rebuild();
    }

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public FontFamily? CodeFontFamily
    {
        get => (FontFamily?)GetValue(CodeFontFamilyProperty);
        set => SetValue(CodeFontFamilyProperty, value);
    }

    /// <summary>The text as the bubble shows it, markers dropped — what a copy of the whole bubble carries.</summary>
    public string PlainText => MarkdownLite.ToPlainText(Source);

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // The document does not follow the control's font, weight and ink on its own.
        if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == FontWeightProperty
            || e.Property == FontStyleProperty || e.Property == ForegroundProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var blocks = MarkdownLite.Parse(Source);
        BuildTwin(blocks);
        BuildDocument(blocks);
    }

    private void BuildTwin(IReadOnlyList<MarkdownBlock> blocks)
    {
        _twin.FontFamily = FontFamily;
        _twin.FontSize = FontSize;
        _twin.FontWeight = FontWeight;
        _twin.FontStyle = FontStyle;
        _twin.LineHeight = LineHeight;
        _twin.Inlines.Clear();

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (i > 0)
            {
                _twin.Inlines.Add(new LineBreak());
                // A paragraph gap, approximated by an empty line of a smaller size.
                _twin.Inlines.Add(new Run(" ") { FontSize = Math.Max(1, ParagraphGap) });
                _twin.Inlines.Add(new LineBreak());
            }

            if (block.Kind is MarkdownBlockKind.Bullet or MarkdownBlockKind.Numbered)
                _twin.Inlines.Add(new Run(new string(' ', block.Indent * 4) + (block.Kind == MarkdownBlockKind.Bullet ? "• " : block.Number + ". ")));

            foreach (var run in block.Runs)
                _twin.Inlines.Add(run.IsLineBreak ? new LineBreak() : Inline(run, block));
        }
    }

    private void BuildDocument(IReadOnlyList<MarkdownBlock> blocks)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontStyle = FontStyle,
            Foreground = Foreground,
            TextAlignment = TextAlignment.Left,
            LineHeight = LineHeight,
        };

        List? list = null;
        var listIndent = -1;
        var listKind = MarkdownBlockKind.Paragraph;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var last = i == blocks.Count - 1;

            if (!IsListBlock(block.Kind))
            {
                list = null;
                listIndent = -1;
                document.Blocks.Add(Paragraph(block, new Thickness(0, 0, 0, last ? 0 : ParagraphGap)));
                continue;
            }

            if (list is null || listIndent != block.Indent || listKind != block.Kind)
            {
                list = NewList(block);
                listIndent = block.Indent;
                listKind = block.Kind;
                document.Blocks.Add(list);
            }

            list.ListItems.Add(NewListItem(block, last));
            var closesList = !last && !IsListBlock(blocks[i + 1].Kind);
            if (closesList)
                list.Margin = new Thickness(list.Margin.Left, 0, 0, ParagraphGap);
        }

        _text.Document = document;
    }

    private static bool IsListBlock(MarkdownBlockKind kind) =>
        kind is MarkdownBlockKind.Bullet or MarkdownBlockKind.Numbered;

    /// <summary>A list opened by its first item: bullets or numbers, indented by the item's level.</summary>
    private static List NewList(MarkdownBlock first) => new()
    {
        MarkerStyle = first.Kind == MarkdownBlockKind.Bullet ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal,
        StartIndex = first.Kind == MarkdownBlockKind.Numbered ? Math.Max(1, first.Number) : 1,
        Margin = new Thickness(first.Indent * ListIndent, 0, 0, 0),
        Padding = new Thickness(ListIndent, 0, 0, 0),
        MarkerOffset = 6,
    };

    private ListItem NewListItem(MarkdownBlock block, bool last)
    {
        var item = new ListItem { Margin = new Thickness(0, 0, 0, last ? 0 : 2) };
        item.Blocks.Add(Paragraph(block, new Thickness(0)));
        return item;
    }

    private Paragraph Paragraph(MarkdownBlock block, Thickness margin)
    {
        var paragraph = new Paragraph { Margin = margin, TextIndent = 0 };
        if (block.Kind == MarkdownBlockKind.Heading)
        {
            paragraph.FontWeight = FontWeights.Bold;
            paragraph.FontSize = FontSize * HeadingScale(block.Level);
        }

        foreach (var run in block.Runs)
            paragraph.Inlines.Add(run.IsLineBreak ? new LineBreak() : Inline(run, block));

        return paragraph;
    }

    private static double HeadingScale(int level) => level switch
    {
        1 => 1.2,
        2 => 1.1,
        _ => 1.0,
    };

    private Run Inline(MarkdownRun run, MarkdownBlock block)
    {
        var inline = new Run(run.Text);
        if (run.Bold || block.Kind == MarkdownBlockKind.Heading)
            inline.FontWeight = FontWeights.Bold;
        if (run.Italic)
            inline.FontStyle = FontStyles.Italic;
        if (run.Code && CodeFontFamily is { } mono)
            inline.FontFamily = mono;
        return inline;
    }
}
