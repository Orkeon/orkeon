using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>
/// The one place the section screens agree on shape: caption column, field column, and
/// the three-state check box that mirrors an optional setting (unset / on / off).
/// </summary>
internal static class FormLayout
{
    /// <summary>Width of the caption column, in cells.</summary>
    public const int CaptionWidth = 30;

    /// <summary>Left margin of every row.</summary>
    public const int Margin = 1;

    /// <summary>
    /// Adds a full-width line of fixed text (a heading or a hint). The label is owned by
    /// <paramref name="parent"/> and never read back, so no reference is handed out.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership transfers to the parent view: Terminal.Gui disposes the subviews " +
                        "it was given when the parent is disposed, which is why no reference is returned.")]
    public static void AddNote(View parent, int y, string text)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var label = new Label
        {
            X = Margin,
            Y = y,
            Width = Dim.Fill(Margin),
            Text = text,
        };

        parent.Add(label);
    }

    /// <summary>Adds a full-width line of text whose content changes as the form is edited.</summary>
    public static Label AddText(View parent, int y, string text)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var label = new Label
        {
            X = Margin,
            Y = y,
            Width = Dim.Fill(Margin),
            Text = text,
        };

        parent.Add(label);
        return label;
    }

    /// <summary>Adds a captioned text field and returns the field.</summary>
    public static TextField AddField(View parent, int y, string caption, string value, bool secret = false)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var label = new Label { X = Margin, Y = y, Text = caption };
        var field = new TextField
        {
            X = Margin + CaptionWidth,
            Y = y,
            Width = Dim.Fill(Margin),
            Text = value,
            Secret = secret,
        };

        parent.Add(label, field);
        return field;
    }

    /// <summary>
    /// Adds a check box bound to an optional boolean: no check state means "no key in the
    /// file", which is not the same thing as an explicit <c>false</c>.
    /// </summary>
    public static CheckBox AddOptionalSwitch(View parent, int y, string caption, bool? value)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var checkBox = new CheckBox
        {
            X = Margin,
            Y = y,
            Width = Dim.Fill(Margin),
            Text = caption,
            AllowCheckStateNone = true,
            Value = ToCheckState(value),
        };

        parent.Add(checkBox);
        return checkBox;
    }

    /// <summary>Adds a captioned closed list and returns the list view.</summary>
    public static ListView AddChoiceList(
        View parent,
        int y,
        int height,
        string caption,
        IReadOnlyList<string> choices,
        int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(choices);

        var label = new Label { X = Margin, Y = y, Text = caption };
        var list = new ListView
        {
            X = Margin + CaptionWidth,
            Y = y,
            Width = Dim.Fill(Margin),
            Height = height,
        };

        list.SetSource(new ObservableCollection<string>(choices));
        if (choices.Count > 0)
            list.SelectedItem = Math.Clamp(selectedIndex, 0, choices.Count - 1);

        parent.Add(label, list);
        return list;
    }

    /// <summary>Replaces the contents of a list, keeping the selection in range.</summary>
    public static void SetItems(ListView list, IReadOnlyList<string> items, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(items);

        list.SetSource(new ObservableCollection<string>(items));
        if (items.Count > 0)
            list.SelectedItem = Math.Clamp(selectedIndex, 0, items.Count - 1);
    }

    /// <summary>The selected index of a list, or -1 when nothing is selected.</summary>
    public static int SelectedIndex(ListView list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list.SelectedItem ?? -1;
    }

    /// <summary>Maps an optional boolean onto a check state.</summary>
    public static CheckState ToCheckState(bool? value) => value switch
    {
        true => CheckState.Checked,
        false => CheckState.UnChecked,
        null => CheckState.None,
    };

    /// <summary>Maps a check state back onto an optional boolean.</summary>
    public static bool? ToBoolean(CheckState state) => state switch
    {
        CheckState.Checked => true,
        CheckState.UnChecked => false,
        _ => null,
    };
}
