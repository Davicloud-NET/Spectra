using Avalonia.Input;
using SpectraEngine.Core.Input;
using SpectraEngine.Editor.Viewport;
using System.Linq;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using EngineKeyModifiers = SpectraEngine.Core.Input.KeyModifiers;

namespace SpectraEngine.Editor.Tests;

/// <summary>Avalonia keys to engine keys, checked by name.</summary>
public sealed class AvaloniaKeyTests
{
    // Keys the two enums spell differently. Strings, not enum members, so this
    // is a second transcription of the table and a transposition shows up.
    private static readonly (string Avalonia, string Engine)[] Renamed =
        [
            // Written out, not a range: an off-by-one here fires the wrong
            // Ctrl+digit insert.
            ("D0", "Number0"),
            ("D1", "Number1"),
            ("D2", "Number2"),
            ("D3", "Number3"),
            ("D4", "Number4"),
            ("D5", "Number5"),
            ("D6", "Number6"),
            ("D7", "Number7"),
            ("D8", "Number8"),
            ("D9", "Number9"),

            // Same for the keypad, which the view shortcuts use.
            ("NumPad0", "Keypad0"),
            ("NumPad1", "Keypad1"),
            ("NumPad2", "Keypad2"),
            ("NumPad3", "Keypad3"),
            ("NumPad4", "Keypad4"),
            ("NumPad5", "Keypad5"),
            ("NumPad6", "Keypad6"),
            ("NumPad7", "Keypad7"),
            ("NumPad8", "Keypad8"),
            ("NumPad9", "Keypad9"),

            ("Return", "Enter"),
            ("Back", "Backspace"),
            ("Scroll", "ScrollLock"),
            ("LWin", "SuperLeft"),
            ("RWin", "SuperRight"),
            ("Apps", "Menu"),
            ("LeftShift", "ShiftLeft"),
            ("RightShift", "ShiftRight"),
            ("LeftCtrl", "ControlLeft"),
            ("RightCtrl", "ControlRight"),
            ("LeftAlt", "AltLeft"),
            ("RightAlt", "AltRight"),
            ("OemQuotes", "Apostrophe"),
            ("OemComma", "Comma"),
            ("OemMinus", "Minus"),
            ("OemPeriod", "Period"),
            ("OemQuestion", "Slash"),
            ("OemSemicolon", "Semicolon"),
            ("OemPlus", "Equal"),
            ("OemOpenBrackets", "LeftBracket"),
            ("OemPipe", "BackSlash"),
            ("OemCloseBrackets", "RightBracket"),
            ("OemTilde", "GraveAccent"),
        ];

    /// <summary>The renamed key pairs, as theory rows.</summary>
    public static TheoryData<string, string> RenamedKeys
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach ((string avalonia, string engine) in Renamed)
                rows.Add(avalonia, engine);
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(RenamedKeys))]
    public void A_renamed_key_maps_to_the_engine_name_this_test_says_it_should(
        string avaloniaName, string engineName)
    {
        Enum.TryParse(avaloniaName, out Key key).ShouldBeTrue(
            $"Avalonia has no key named {avaloniaName}");
        Enum.TryParse(engineName, out InputKey expected).ShouldBeTrue(
            $"the engine has no key named {engineName}");

        AvaloniaKeys.ToInputKey(key).ShouldBe(expected);
    }

    [Fact]
    public void A_key_both_enums_spell_the_same_way_keeps_its_name()
    {
        foreach (InputKey engineKey in Enum.GetValues<InputKey>())
        {
            if (engineKey is InputKey.Unknown ||
                Renamed.Any(row => row.Engine == engineKey.ToString()))
            {
                continue;
            }

            Enum.TryParse(engineKey.ToString(), out Key key).ShouldBeTrue(
                $"InputKey.{engineKey} has no Avalonia key of the same name, so it belongs in the " +
                "renamed table rather than being left out of both");
            AvaloniaKeys.ToInputKey(key).ShouldBe(engineKey);
        }
    }

    [Fact]
    public void Every_key_the_engine_names_can_actually_be_produced()
    {
        // Catches an omission: an unmapped key becomes Unknown and its shortcut
        // stops working with nothing logged.
        var reachable = new HashSet<InputKey>();
        foreach (Key key in Enum.GetValues<Key>())
        {
            InputKey mapped = AvaloniaKeys.ToInputKey(key);
            if (mapped is not InputKey.Unknown)
                reachable.Add(mapped);
        }

        foreach (InputKey engineKey in Enum.GetValues<InputKey>())
        {
            if (engineKey is InputKey.Unknown)
                continue;

            reachable.ShouldContain(engineKey, $"no Avalonia key maps to InputKey.{engineKey}");
        }
    }

    [Theory]
    [InlineData(Key.A, InputKey.A)]
    [InlineData(Key.W, InputKey.W)]
    [InlineData(Key.Z, InputKey.Z)]
    public void The_letter_row_maps_by_range(Key key, InputKey expected) =>
        AvaloniaKeys.ToInputKey(key).ShouldBe(expected);

    [Theory]
    [InlineData(Key.D0, InputKey.Number0)]
    [InlineData(Key.D3, InputKey.Number3)]
    [InlineData(Key.D9, InputKey.Number9)]
    public void The_digit_row_maps_by_range(Key key, InputKey expected) =>
        AvaloniaKeys.ToInputKey(key).ShouldBe(expected);

    [Theory]
    [InlineData(Key.F1, InputKey.F1)]
    [InlineData(Key.F8, InputKey.F8)]
    [InlineData(Key.F12, InputKey.F12)]
    public void The_function_row_maps_by_range(Key key, InputKey expected) =>
        AvaloniaKeys.ToInputKey(key).ShouldBe(expected);

    [Fact]
    public void A_key_the_engine_does_not_name_is_unknown_rather_than_nearby()
    {
        AvaloniaKeys.ToInputKey(Key.F20).ShouldBe(InputKey.Unknown);
        AvaloniaKeys.ToInputKey(Key.None).ShouldBe(InputKey.Unknown);

        // Keypad operators have no engine name. Keypad digits do.
        AvaloniaKeys.ToInputKey(Key.Multiply).ShouldBe(InputKey.Unknown);
        AvaloniaKeys.ToInputKey(Key.Divide).ShouldBe(InputKey.Unknown);
    }

    [Theory]
    [InlineData(Key.NumPad0, InputKey.Keypad0)]
    [InlineData(Key.NumPad7, InputKey.Keypad7)]
    [InlineData(Key.NumPad9, InputKey.Keypad9)]
    public void The_keypad_maps_by_range(Key key, InputKey expected)
    {
        // Must not map to the number row: those keys are the tools and inserts.
        AvaloniaKeys.ToInputKey(key).ShouldBe(expected);
        AvaloniaKeys.ToInputKey(Key.NumPad7).ShouldNotBe(InputKey.Number7);
    }

    [Fact]
    public void Modifiers_translate_including_the_super_rename()
    {
        AvaloniaKeys.ToModifiers(AvaloniaKeyModifiers.None).ShouldBe(EngineKeyModifiers.None);
        AvaloniaKeys.ToModifiers(AvaloniaKeyModifiers.Shift).ShouldBe(EngineKeyModifiers.Shift);
        AvaloniaKeys.ToModifiers(AvaloniaKeyModifiers.Control).ShouldBe(EngineKeyModifiers.Control);
        AvaloniaKeys.ToModifiers(AvaloniaKeyModifiers.Alt).ShouldBe(EngineKeyModifiers.Alt);
        AvaloniaKeys.ToModifiers(AvaloniaKeyModifiers.Meta).ShouldBe(EngineKeyModifiers.Super);

        AvaloniaKeys.ToModifiers(AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift)
            .ShouldBe(EngineKeyModifiers.Control | EngineKeyModifiers.Shift);
    }

    [Fact]
    public void The_three_mouse_buttons_map_and_the_rest_do_not()
    {
        AvaloniaKeys.ToPointerButton(PointerUpdateKind.LeftButtonPressed)
            .ShouldBe(PointerButtons.Left);
        AvaloniaKeys.ToPointerButton(PointerUpdateKind.LeftButtonReleased)
            .ShouldBe(PointerButtons.Left);
        AvaloniaKeys.ToPointerButton(PointerUpdateKind.RightButtonPressed)
            .ShouldBe(PointerButtons.Right);
        AvaloniaKeys.ToPointerButton(PointerUpdateKind.MiddleButtonPressed)
            .ShouldBe(PointerButtons.Middle);

        AvaloniaKeys.ToPointerButton(PointerUpdateKind.Other).ShouldBe(PointerButtons.None);
        AvaloniaKeys.ToPointerButton(PointerUpdateKind.XButton1Pressed).ShouldBe(PointerButtons.None);
    }

    [Fact]
    public void Cursor_shapes_degrade_to_the_nearest_thing_the_platform_has()
    {
        AvaloniaKeys.ToStandardCursor(CursorShape.Arrow).ShouldBe(StandardCursorType.Arrow);
        AvaloniaKeys.ToStandardCursor(CursorShape.Crosshair).ShouldBe(StandardCursorType.Cross);

        // The standard set has no grab or rotate cursor.
        AvaloniaKeys.ToStandardCursor(CursorShape.Grab).ShouldBe(StandardCursorType.Hand);
        AvaloniaKeys.ToStandardCursor(CursorShape.Rotate).ShouldBe(StandardCursorType.SizeAll);
    }
}
