using System.Linq;
using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace Spectra.Kitchen.Tests;

// The fixture's room with tags and attributes on a world brush, a part, an
// entity, a light and a folder. Between them the attributes are of every
// kind, and one tag is on several nodes in an order that is not the tree's.
internal static class TaggedRoom
{
    public static readonly CFrame Rest = new(
        new Vector3(1f, 2.5f, -3f), Quaternion.CreateFromYawPitchRoll(0.6f, -0.2f, 1.1f));

    // A letter outside ASCII, by its code point.
    public static readonly string Heiss = $"hei{(char)0xDF}";

    public static Scene Build(MapFixture fixture)
    {
        Scene scene = fixture.BuildScene(withEntities: true, withLights: true);

        Named(scene, "Crate").Tags = TagSet.Of("Lava", "Hot stuff");
        Named(scene, "Crate").Attributes = AttributeSet.Empty
            .With("Damage", MemberValue.Of(25d))
            .With("Label", MemberValue.Of(Heiss))
            .With("Deadly", MemberValue.Of(true))
            .With("Push", MemberValue.Of(new Vector3(0f, 12.5f, -1f)))
            .With("Glow", MemberValue.Of(new Color3(1f, 0.5f, 0f)))
            .With("Rest", MemberValue.Of(Rest));

        Named(scene, "Floor").Tags = TagSet.Of("Ground", "Lava");
        Named(scene, "Floor").Attributes = AttributeSet.Empty
            .With("Tenth", MemberValue.Of(0.1d))
            .With("Safe", MemberValue.Of(false));

        Named(scene, "Door").Tags = TagSet.Of("Lava");
        Named(scene, "Door").Attributes = AttributeSet.Empty.With("Owner", MemberValue.Of(""));

        Named(scene, "Sun").Tags = TagSet.Of("Sky");

        SceneNode shelf = scene.Root.CreateChild("Shelf");
        shelf.Tags = TagSet.Of("Lava", "Empty");
        return scene;
    }

    public static SceneNode Named(Scene scene, string name) =>
        scene.Root.Traverse().Single(node => node.Name == name);
}
