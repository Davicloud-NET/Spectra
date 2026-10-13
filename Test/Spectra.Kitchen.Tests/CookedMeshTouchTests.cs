using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Scripting.Luau;
using System;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Where a level from its pack cannot do what its loose files do for a
/// touch: a baked map loads no mesh, so nothing can touch one, and the
/// level has to say so of that node and of no other.
/// </summary>
[Trait("Suite", RealScriptCheck.Suite)]
public class CookedMeshTouchTests
{
    [Fact]
    public void The_level_from_its_pack_says_of_its_mesh_that_nothing_can_touch_it_and_the_loose_one_of_no_node()
    {
        RealScriptCheck.Require();
        using TempProject project = AskedLevel.Write();

        // OpenGL only: that is what the fake renderer reports.
        CookResult cook = new CookSession(
            project.Layout,
            new CookSettings
            {
                UseCache = false,
                Targets = [GraphicsBackend.OpenGL],
                ScriptCheck = new LuauSyntaxCheck(),
            }).Run();
        cook.Succeeded.ShouldBeTrue(string.Join(Environment.NewLine, cook.Diagnostics));

        using (PlayedLevel loose = PlayedLevel.Loose(project, AskedLevel.Bundle))
            Untouchable(loose.Scene).ShouldBeEmpty();

        using (PlayedLevel cooked = PlayedLevel.Cooked(project, AskedLevel.Bundle))
        {
            Untouchable(cooked.Scene).ShouldBe(["Prop"]);
            SceneNode prop = cooked.Scene.Root.Traverse().Single(node => node.Name == "Prop");
            TouchSignal.WhyNotHere(prop).ShouldNotBeNull().ShouldStartWith(
                "Prop is a mesh, and a baked map does not load a mesh yet. Nothing can touch it in this level");
        }
    }

    private static string[] Untouchable(Scene scene) =>
        [.. scene.Root.Traverse().Where(node => TouchSignal.WhyNotHere(node) is not null).Select(node => node.Name)];
}
