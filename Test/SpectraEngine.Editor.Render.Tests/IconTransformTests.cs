using Avalonia;
using Avalonia.Media;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// The one thing about the icon pipeline that no icon in the repository
/// currently exercises.
/// </summary>
/// <remarks>
/// <para>
/// Every glyph shipped today is authored on the box it renders at, so the
/// generator emits no transform for any of them and the transform path is dead
/// code until somebody drops in artwork on a different viewBox - which is
/// precisely the person the whole mechanism exists for.
/// </para>
/// <para>
/// It was written as <c>Transform="scale(0.5)"</c> first. That compiles, and it
/// survives a build, and it survives every icon that needs no transform,
/// because Avalonia's converter is <see cref="Matrix"/>.Parse and the failure
/// is a <see cref="FormatException"/> raised when the resource is first USED.
/// It was found by re-authoring one glyph on a 64 box and rendering, and this
/// is that finding kept.
/// </para>
/// </remarks>
public sealed class IconTransformTests
{
    [Fact]
    public void The_generator_emits_a_transform_Avalonia_can_actually_parse()
    {
        // The exact shape Icons.targets writes: uniform scale, then offset.
        Transform.Parse("0.5,0,0,0.5,0,0").Value.ShouldBe(Matrix.CreateScale(0.5, 0.5));
        Transform.Parse("0.66667,0,0,0.66667,0,4").ShouldNotBeNull();
    }

    [Fact]
    public void A_css_shaped_transform_is_refused_which_is_why_the_matrix_form_is_used()
    {
        // Falsification, kept: this is what the generator used to write.
        Should.Throw<FormatException>(() => Transform.Parse("scale(0.5)"));
    }
}
