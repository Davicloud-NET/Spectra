using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.TextFormatting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// A row's label in the Properties panel. While an ellipsis hides part of the
/// name, the tooltip says it in full.
/// </summary>
public sealed class RowLabel : TextBlock
{
    /// <summary>Defines <see cref="Help"/>.</summary>
    public static readonly StyledProperty<string?> HelpProperty =
        AvaloniaProperty.Register<RowLabel, string?>(nameof(Help));

    /// <summary>Defines <see cref="Hint"/>.</summary>
    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<RowLabel, string?>(nameof(Hint));

    /// <summary>One line explaining the setting. The tooltip says it under the name.</summary>
    public string? Help
    {
        get => GetValue(HelpProperty);
        set => SetValue(HelpProperty, value);
    }

    /// <summary>How the label is used as a handle. The tooltip's last line.</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Whether an ellipsis hides part of the text.</summary>
    public bool IsTrimmed { get; private set; }

    /// <inheritdoc/>
    // Styled as the TextBlock it is, so the panel's TextBlock.rowlabel styles apply.
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);

        // The text is laid out again for every arrange, so this is where the
        // ellipsis comes and goes.
        bool trimmed = HasCollapsedLine();
        if (trimmed != IsTrimmed)
        {
            IsTrimmed = trimmed;
            RefreshTip();
        }

        return arranged;
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty || change.Property == HelpProperty || change.Property == HintProperty)
            RefreshTip();
    }

    private bool HasCollapsedLine()
    {
        IReadOnlyList<TextLine> lines = TextLayout.TextLines;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].HasCollapsed)
                return true;
        }

        return false;
    }

    // No tip at all when there is nothing to say, or an empty one would open.
    private void RefreshTip()
    {
        string?[] lines = [IsTrimmed ? Text : null, Help, Hint];
        string tip = string.Join('\n', lines.Where(line => !string.IsNullOrEmpty(line)));

        ToolTip.SetTip(this, tip.Length > 0 ? tip : null);
    }
}
