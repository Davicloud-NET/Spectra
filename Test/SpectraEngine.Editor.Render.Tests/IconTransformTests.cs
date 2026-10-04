using Avalonia;
using Avalonia.Media;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>The transform form the icon generator emits for artwork on another viewBox.</summary>
// No shipped icon needs a transform, so nothing else exercises this path.
// Runs on the session thread: calling Avalonia from xunit's thread first
// initialises the platform there and breaks the session for every other test.
[Collection(RibbonSessionCollection.Name)]
public sealed class IconTransformTests(RibbonSession session)
{
    [Fact]
    public void The_generator_emits_a_transform_Avalonia_can_actually_parse()
    {
        session.On(() =>
        {
            // What Icons.targets writes: uniform scale, then offset.
            Transform.Parse("0.5,0,0,0.5,0,0").Value.ShouldBe(Matrix.CreateScale(0.5, 0.5));
            Transform.Parse("0.66667,0,0,0.66667,0,4").ShouldNotBeNull();
        });
    }

    [Fact]
    public void A_css_shaped_transform_is_refused_which_is_why_the_matrix_form_is_used()
    {
        // Avalonia's Transform converter is Matrix.Parse. It fails on first use, not at build.
        session.On(() => Should.Throw<FormatException>(() => Transform.Parse("scale(0.5)")));
    }
}
