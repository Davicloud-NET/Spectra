using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>One line of the keyboard reference.</summary>
public sealed record KeyboardRow(string Keys, string What);

/// <summary>One headed group of the keyboard reference.</summary>
public sealed record KeyboardSection(string Name, IReadOnlyList<KeyboardRow> Rows);

/// <summary>
/// The shell's keyboard reference: every chord the editor answers to, grouped.
/// </summary>
// Written by hand: the chords live in the window's KeyBindings, the viewport's
// ShellChord interception and the engine keymap, so no one table can generate it.
public partial class KeyboardReferenceWindow : Window
{
    public KeyboardReferenceWindow()
    {
        InitializeComponent();
        DataContext = this;
        Opened += (_, _) => DarkCaption.Apply(this);
    }

    /// <summary>The reference, in reading order.</summary>
    public IReadOnlyList<KeyboardSection> Sections { get; } =
    [
        new("Getting around", [
            new("Right-drag", "Look. Hold it and use W A S D, Q and E to fly."),
            new("Alt + drag", "Orbit the selection."),
            new("Middle-drag", "Pan."),
            new("Wheel", "Zoom toward the cursor."),
            new("F", "Frame the selection."),
            new("Shift + F", "Frame everything."),
            new("F7", "Switch between the editor camera and the engine fly camera."),
            new("Numpad 7, 1, 3", "Top, front and right views, looking straight on with no perspective."),
            new("Ctrl + Numpad 7, 1, 3", "Bottom, back and left."),
            new("Numpad 5", "Back to perspective. Looking around or orbiting also does."),
        ]),

        new("Selecting", [
            new("Click", "Select what is under the cursor."),
            new("Ctrl + click", "Add to or remove from the selection."),
            new("Ctrl + A", "Select every top-level object."),
            new("Drag on empty space", "Box select."),
            new("Right-click", "Open the scene menu on whatever is under the cursor."),
            new("Esc", "Clear the selection."),
        ]),

        new("Building", [
            new("Ctrl + 1", "Insert a block: solid geometry that merges with the level."),
            new("Ctrl + 2", "Insert a part: moves freely, never merges."),
            new("Ctrl + 3", "Insert a cut: carves a hole out of the blocks it overlaps."),
            new("Ctrl + 4", "Insert a light."),
            new("Ctrl + D", "Duplicate the selection."),
            new("Del", "Delete the selection."),
            new("Ctrl + G", "Group the selection."),
            new("Ctrl + Shift + G", "Ungroup."),
            new("Ctrl + T", "Convert the selection between block and part."),
            new("F2", "Rename, with the scene tree focused."),
        ]),

        new("Moving things", [
            new("2  or  W", "Move tool."),
            new("3  or  R", "Size tool."),
            new("4  or  E", "Rotate tool."),
            new("X", "Swap the drag axes between world and local."),
            new("Y", "Swap the handles between Studio and Classic."),
            new("Shift (while sizing)", "Ask for the other anchoring for one drag."),
            new("G", "Snap to the grid."),
            new("[  and  ]", "Finer and coarser grid."),
            new("Alt (while dragging)", "Invert snapping for that one drag."),
            new("Esc (while dragging)", "Cancel the drag and put it back."),
        ]),

        new("History and files", [
            new("Ctrl + Z", "Undo."),
            new("Ctrl + Y", "Redo. Ctrl + Shift + Z does the same."),
            new("Ctrl + S", "Save the level."),
            new("Ctrl + Shift + S", "Save as."),
            new("Ctrl + N", "New level."),
            new("Ctrl + O", "Open a level."),
        ]),

        new("Looking at the world", [
            new("F8", "Walk the level in first person. F8 or Esc leaves."),
            new("E", "Use what you are looking at, while playing."),
            new("`", "Open the console. While playing it frees the mouse, and a click in the view takes it back. On a German keyboard this key is ö."),
            new("F9", "Draw the character capsule, while playing."),
            new("F1 - F5", "Wireframe, CSG vertices, bounds, face normals, node axes."),
            new("F6", "Cycle the rendering pipeline."),
        ]),

        new("Logic view", [
            new("Ctrl + L", "Show or hide the Logic view."),
            new("Click a card", "Select its entity."),
            new("Ctrl + click a card", "Add its entity to the selection."),
            new("Double-click a card", "Frame its entity in the viewport."),
            new("Drag from a card or an output", "Pull a wire. Drop it on a card and pick what it sends."),
            new("Esc (while dragging)", "Give the wire up."),
            new("Click a wire", "Select it and the entity that sends it."),
            new("Del", "Remove the selected wire."),
            new("Right-click a wire", "Open its menu."),
            new("Drag on empty space", "Pan. Middle-drag pans from anywhere."),
            new("Wheel", "Zoom toward the cursor."),
        ]),
    ];

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
