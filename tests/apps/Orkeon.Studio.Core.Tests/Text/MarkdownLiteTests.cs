using Orkeon.Studio.Core.Text;

namespace Orkeon.Studio.Core.Tests.Text;

/// <summary>
/// The little Markdown a chat bubble renders (STUDIO-57): what the assistant writes — bold
/// labels, bullet lists, a question in bold — read into blocks and runs, and plain text left
/// exactly as typed.
/// </summary>
public sealed class MarkdownLiteTests
{
    private const string AssistantMessage =
        "Merci pour votre demande ! Je comprends l'ensemble :\n\n"
        + "- **Entrée** : deux dossiers contenant des PDF — un avec des instructions\n"
        + "- **Sortie** : des emails envoyés avec les pièces jointes\n\n"
        + "Une chose n'est pas claire pour moi : **comment sait-on quel document va à quel destinataire ?** Est-ce que…";

    [Fact]
    public void The_assistants_message_reads_as_a_paragraph_two_bullets_and_a_paragraph_with_bold()
    {
        var blocks = MarkdownLite.Parse(AssistantMessage);

        Assert.Equal(
            [MarkdownBlockKind.Paragraph, MarkdownBlockKind.Bullet, MarkdownBlockKind.Bullet, MarkdownBlockKind.Paragraph],
            blocks.Select(b => b.Kind));
        Assert.Equal("Merci pour votre demande ! Je comprends l'ensemble :", blocks[0].PlainText);

        var entry = blocks[1].Runs;
        Assert.Equal(new MarkdownRun("Entrée", Bold: true), entry[0]);
        Assert.Equal(" : deux dossiers contenant des PDF — un avec des instructions", entry[1].Text);
        Assert.False(entry[1].Bold);

        var question = blocks[3].Runs;
        Assert.Equal("Une chose n'est pas claire pour moi : ", question[0].Text);
        Assert.Equal(new MarkdownRun("comment sait-on quel document va à quel destinataire ?", Bold: true), question[1]);
        Assert.True(MarkdownLite.LooksLikeMarkdown(AssistantMessage));
    }

    [Fact]
    public void Plain_text_is_one_paragraph_as_typed_and_is_no_markdown()
    {
        var blocks = MarkdownLite.Parse("c'est dans les instructions");

        var block = Assert.Single(blocks);
        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(new MarkdownRun("c'est dans les instructions"), Assert.Single(block.Runs));
        Assert.False(MarkdownLite.LooksLikeMarkdown("c'est dans les instructions"));
        Assert.Empty(MarkdownLite.Parse(null));
        Assert.Empty(MarkdownLite.Parse("  \n "));
    }

    [Fact]
    public void A_line_break_inside_a_paragraph_is_kept_as_one()
    {
        var block = Assert.Single(MarkdownLite.Parse("première ligne\nseconde ligne"));

        Assert.Equal(["première ligne", "\n", "seconde ligne"], block.Runs.Select(r => r.Text));
        Assert.True(block.Runs[1].IsLineBreak);
    }

    [Fact]
    public void Italic_code_links_and_escapes_are_read_and_identifiers_are_left_alone()
    {
        var runs = MarkdownLite.ParseInlines("un *mot* en `code`, un [lien](https://x.y) et snake_case_name, \\*pas gras\\*");

        Assert.Equal(
        [
            new MarkdownRun("un "),
            new MarkdownRun("mot", Italic: true),
            new MarkdownRun(" en "),
            new MarkdownRun("code", Code: true),
            new MarkdownRun(", un lien et snake_case_name, *pas gras*"),
        ], runs);

        // An underscore at a word start opens an italic; a lone star followed by a space opens nothing.
        Assert.Equal(
            [new MarkdownRun("souligné", Italic: true), new MarkdownRun(" non fermé * seul")],
            MarkdownLite.ParseInlines("_souligné_ non fermé * seul"));
    }

    [Fact]
    public void Headings_numbered_items_nested_bullets_and_fenced_code_are_blocks_of_their_own()
    {
        var blocks = MarkdownLite.Parse("## Plan\n1. lire\n2) écrire\n- a\n  - b\n```\nx = 1\ny = 2\n```");

        Assert.Equal(
            [MarkdownBlockKind.Heading, MarkdownBlockKind.Numbered, MarkdownBlockKind.Numbered, MarkdownBlockKind.Bullet, MarkdownBlockKind.Bullet, MarkdownBlockKind.Code],
            blocks.Select(b => b.Kind));
        Assert.Equal(2, blocks[0].Level);
        Assert.Equal("Plan", blocks[0].PlainText);
        Assert.Equal([1, 2], blocks.Skip(1).Take(2).Select(b => b.Number));
        Assert.Equal([0, 1], blocks.Skip(3).Take(2).Select(b => b.Indent));
        Assert.Equal("x = 1\ny = 2", blocks[5].PlainText);
        Assert.True(blocks[5].Runs[0].Code);
    }

    [Fact]
    public void Plain_text_of_a_message_drops_the_markers_and_keeps_the_list_shape()
    {
        Assert.Equal(
            "Je comprends :\n- Entrée : des PDF\n- Sortie : des emails\n1. lire",
            MarkdownLite.ToPlainText("Je comprends :\n\n- **Entrée** : des PDF\n- **Sortie** : des emails\n\n1. lire"));
    }

    [Fact]
    public void A_dash_or_a_number_that_opens_no_item_stays_text()
    {
        Assert.Equal(MarkdownBlockKind.Paragraph, Assert.Single(MarkdownLite.Parse("-pas une puce")).Kind);
        Assert.Equal(MarkdownBlockKind.Paragraph, Assert.Single(MarkdownLite.Parse("2026 est une année")).Kind);
        Assert.Equal(MarkdownBlockKind.Paragraph, Assert.Single(MarkdownLite.Parse("#pas un titre")).Kind);
    }
}
