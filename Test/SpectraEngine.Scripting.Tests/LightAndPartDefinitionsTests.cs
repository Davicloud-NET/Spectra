using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The definitions file for luau-lsp: the Color3 type and its library, and
/// the members of a light and of a part on the classes that have them. A
/// member a script can set is not marked as read only.
/// </summary>
public sealed class LightAndPartDefinitionsTests
{
    private static readonly string File = LuauDefinitions.Write(LuauScriptApi.Description);

    [Fact]
    public void A_Color3_is_a_type_with_its_parts_read_only_and_its_three_methods()
    {
        string color = BlockOf("declare extern type Color3 with");

        color.ShouldContain("\n    read R: number\n");
        color.ShouldContain("\n    read G: number\n");
        color.ShouldContain("\n    read B: number\n");
        color.ShouldContain("\n    function Lerp(self, goal: Color3, alpha: number): Color3\n");
        color.ShouldContain("\n    function ToHSV(self): (number, number, number)\n");
        color.ShouldContain("\n    function ToHex(self): string\n");
        color.ShouldNotContain("function __");
        color.ShouldNotContain("[string]");
    }

    [Fact]
    public void The_Color3_library_makes_one_in_four_ways()
    {
        string library = BlockOf("declare Color3: {");

        library.ShouldContain("\n    new: (red: number?, green: number?, blue: number?) -> Color3,\n");
        library.ShouldContain("\n    fromRGB: (red: number?, green: number?, blue: number?) -> Color3,\n");
        library.ShouldContain("\n    fromHSV: (hue: number?, saturation: number?, value: number?) -> Color3,\n");
        library.ShouldContain("\n    fromHex: (hex: string) -> Color3,\n");
    }

    [Fact]
    public void Every_light_can_be_switched_dimmed_and_tinted()
    {
        string light = BlockOf("declare extern type Light extends Instance with");

        light.ShouldContain("\n    Enabled: boolean\n");
        light.ShouldContain("\n    Brightness: number\n");
        light.ShouldContain("\n    Color: Color3\n");
        light.ShouldNotContain("Range");
    }

    [Theory]
    [InlineData("PointLight", true)]
    [InlineData("SpotLight", true)]
    [InlineData("SurfaceLight", true)]
    [InlineData("DiscLight", true)]
    [InlineData("DirectionalLight", false)]
    public void A_range_is_declared_on_each_light_that_has_one(string light, bool hasRange)
    {
        BlockOf($"declare extern type {light} extends Light with").Contains("\n    Range: number\n").ShouldBe(hasRange);
    }

    [Fact]
    public void A_part_and_a_mesh_have_their_four_switches_through_BasePart()
    {
        string part = BlockOf("declare extern type BasePart extends Instance with");

        foreach (string member in new[] { "CanCollide", "CanQuery", "CanTouch", "Visible" })
            part.ShouldContain($"\n    {member}: boolean\n");

        BlockOf("declare extern type Part extends BasePart with").ShouldNotContain("CanCollide");
        BlockOf("declare extern type MeshPart extends BasePart with").ShouldNotContain("CanCollide");
    }

    [Fact]
    public void Every_node_can_be_asked_for_an_attribute_of_any_type()
    {
        BlockOf("declare extern type Instance with")
            .ShouldContain("\n    function GetAttribute(self, attribute: string): any\n");
    }

    // From the line that opens a declaration to the line that closes it.
    private static string BlockOf(string opening)
    {
        int at = File.IndexOf(opening + "\n", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, opening);

        string close = opening.EndsWith('{') ? "\n}\n" : "\nend\n";
        return File[at..(File.IndexOf(close, at, StringComparison.Ordinal) + close.Length)];
    }
}
