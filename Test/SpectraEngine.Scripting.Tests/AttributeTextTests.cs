using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// An attribute's text is kept as the bytes of UTF-8 a script hands over,
/// and handed back as the same bytes, whether it is short enough to be kept
/// in the value or not.
/// </summary>
public sealed class AttributeTextTests
{
    // Each case is Luau that makes a string. The longest is the most an attribute holds.
    private const string Cases = """
        local cases = {
            "",
            "a\0b",
            "\0",
            "\u{E9}\u{20AC}\u{1F600}",
            "\u{10FFFF}\u{FFFF}",
            string.rep("x", 28),
            string.rep("x", 29),
            string.rep("\u{E9}", 14),
            string.rep("\u{20AC}", 10),
            string.rep("\0", 1000),
            string.rep("y", 65536),
        }
        """;

    [Fact]
    public void Text_a_script_sets_comes_back_byte_for_byte()
    {
        string[] printed = ScriptLevel.Run(Cases + """

            local part = Instance.new("Folder")
            for index, text in cases do
                part:SetAttribute("T", text)
                local back = part:GetAttribute("T")
                print(#back == #text and back == text and part:GetAttributes().T == text)
            end
            """);

        printed.ShouldBe(Enumerable.Repeat("true", 11));
    }

    [Fact]
    public void A_clone_and_a_destroyed_node_hand_back_the_same_bytes()
    {
        string[] printed = ScriptLevel.Run(Cases + """

            local part = Instance.new("Folder")
            part.Parent = workspace
            for index, text in cases do
                part:SetAttribute("T" .. index, text)
            end
            local copy = part:Clone()
            part:Destroy()
            local same = true
            for index, text in cases do
                same = same and copy:GetAttribute("T" .. index) == text and part:GetAttribute("T" .. index) == text
            end
            print(same)
            """);

        printed.ShouldBe(["true"]);
    }

    [Fact]
    public void Setting_text_equal_to_what_is_held_fires_nothing()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, """
            local part = Instance.new("Folder")
            part.Parent = workspace
            local long = string.rep("z", 100)
            part:SetAttribute("Short", "a\0b")
            part:SetAttribute("Long", long)
            local fired = 0
            part.AttributeChanged:Connect(function() fired += 1 end)
            part:SetAttribute("Short", "a" .. "\0" .. "b")
            part:SetAttribute("Long", string.rep("z", 100))
            task.wait()
            print(fired)
            part:SetAttribute("Short", "a\0c")
            task.wait()
            print(fired)
            """);

        level.Play().Tick(4);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["0", "1"]);
    }

    [Fact]
    public void Text_a_level_was_built_with_reads_as_its_bytes_of_UTF8()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode sign = level.Scene.Root.CreateChild("Sign");
        sign.Attributes = AttributeSet.Empty
            .With("Short", MemberValue.Of("\u00e9\u20ac\U0001F600\0"))
            .With("Long", MemberValue.Of(new string('\u00fc', 1000)));
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            print(sign:GetAttribute("Short") == "\u{E9}\u{20AC}\u{1F600}\0")
            print(sign:GetAttribute("Long") == string.rep("\u{FC}", 1000))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true", "true"]);
    }
}
