using SpectraEngine.Core.Input;
using SpectraEngine.Editor.Viewport.Windows;

namespace SpectraEngine.Editor.Tests;

public sealed class Win32KeyTests
{
    private const int VkA = 0x41;
    private const int VkW = 0x57;
    private const int VkZ = 0x5A;
    private const int Vk0 = 0x30;
    private const int Vk3 = 0x33;
    private const int Vk9 = 0x39;

    // lParam bit 24: the extended-key flag, set for right Control and Alt.
    private const nint Plain = 0;
    private const nint Extended = 1 << 24;

    [Theory]
    [InlineData(VkA, InputKey.A)]
    [InlineData(VkW, InputKey.W)]
    [InlineData(VkZ, InputKey.Z)]
    public void The_letter_row_maps_by_range(int virtualKey, InputKey expected) =>
        Win32Keys.ToInputKey(virtualKey, Plain).ShouldBe(expected);

    [Theory]
    [InlineData(Vk0, InputKey.Number0)]
    [InlineData(Vk3, InputKey.Number3)]
    [InlineData(Vk9, InputKey.Number9)]
    public void The_digit_row_maps_by_range(int virtualKey, InputKey expected) =>
        Win32Keys.ToInputKey(virtualKey, Plain).ShouldBe(expected);

    [Fact]
    public void Control_and_alt_are_separated_by_the_extended_bit()
    {
        Win32Keys.ToInputKey(0x11, Plain).ShouldBe(InputKey.ControlLeft);
        Win32Keys.ToInputKey(0x11, Extended).ShouldBe(InputKey.ControlRight);
        Win32Keys.ToInputKey(0x12, Plain).ShouldBe(InputKey.AltLeft);
        Win32Keys.ToInputKey(0x12, Extended).ShouldBe(InputKey.AltRight);
    }

    [Theory]
    [InlineData(0x70, InputKey.F1)]
    [InlineData(0x7B, InputKey.F12)]
    [InlineData(0x1B, InputKey.Escape)]
    [InlineData(0x2E, InputKey.Delete)]
    [InlineData(0x20, InputKey.Space)]
    [InlineData(0x26, InputKey.Up)]
    public void Named_keys_map_to_their_own_names(int virtualKey, InputKey expected) =>
        Win32Keys.ToInputKey(virtualKey, Plain).ShouldBe(expected);

    [Theory]
    [InlineData(0xDB, InputKey.LeftBracket)]
    [InlineData(0xDD, InputKey.RightBracket)]
    public void The_snap_ladder_keys_map(int virtualKey, InputKey expected) =>
        // [ and ] are OEM codes, not ASCII.
        Win32Keys.ToInputKey(virtualKey, Plain).ShouldBe(expected);

    [Fact]
    public void A_key_the_engine_does_not_name_is_unknown_rather_than_nearby()
    {
        // 0x87 is reserved, 0x6A is keypad multiply.
        Win32Keys.ToInputKey(0x87, Plain).ShouldBe(InputKey.Unknown);
        Win32Keys.ToInputKey(0x6A, Plain).ShouldBe(InputKey.Unknown);
    }

    [Theory]
    [InlineData(0x60, InputKey.Keypad0)]
    [InlineData(0x67, InputKey.Keypad7)]
    [InlineData(0x69, InputKey.Keypad9)]
    public void The_keypad_maps_by_range_and_not_onto_the_number_row(int virtualKey, InputKey expected)
    {
        Win32Keys.ToInputKey(virtualKey, Plain).ShouldBe(expected);
        Win32Keys.ToInputKey(0x67, Plain).ShouldNotBe(InputKey.Number7);
    }
}
