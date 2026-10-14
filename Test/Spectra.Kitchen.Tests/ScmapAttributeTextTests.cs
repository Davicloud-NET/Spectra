using System.Linq;
using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// An attribute's text in a baked map: written once into the string table
/// as the UTF-8 the editor keeps, and read back onto the node as the same bytes.
/// </summary>
public class ScmapAttributeTextTests
{
    private static readonly string[] Texts =
    [
        "",
        "\u00e9\u20ac\U0001F600\0",
        new string('x', MemberValue.InlineUtf8Bytes),
        new string('x', MemberValue.InlineUtf8Bytes + 1),
        new string('\u00fc', 1000),
        "line\nbreak \"quoted\" \\ \u2028",
    ];

    [Fact]
    public void Text_of_any_length_comes_out_of_a_bake_as_its_bytes()
    {
        Scene scene = TaggedRoom.Build(MapFixture.Fresh());
        AttributeSet set = AttributeSet.Empty;
        for (int i = 0; i < Texts.Length; i++)
            set = set.With($"Text{i}", MemberValue.Of(Texts[i]));
        TaggedRoom.Named(scene, "Shelf").Attributes = set;

        using CookedLevel level = CookedLevel.Bake(scene);

        AttributeSet back = TaggedRoom.Named(level.Scene, "Shelf").Attributes;
        back.ShouldBe(set);
        for (int i = 0; i < Texts.Length; i++)
        {
            MemberValue value = back[i].Value;
            value.IsUtf8.ShouldBeTrue();
            value.Utf8.ToArray().ShouldBe(Encoding.UTF8.GetBytes(Texts[i]), $"Text{i}");
            value.Text.ShouldBe(Texts[i]);
        }
    }
}
