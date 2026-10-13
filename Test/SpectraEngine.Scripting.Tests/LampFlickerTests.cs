using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A lamp that flickers: one script in a loop with task.wait, played
/// through a play session with no window. What the light holds and what the
/// renderer would draw on every tick, and the lamp as it was built at Stop.
/// </summary>
public sealed class LampFlickerTests
{
    // A tenth of a second at 60 ticks a second.
    private const int TicksPerWait = 6;

    private const string Flicker = """
        local lamp = script.Parent
        local warm, cold = Color3.fromRGB(255, 180, 80), Color3.fromRGB(80, 120, 255)
        while true do
            lamp.Enabled = not lamp.Enabled
            lamp.Brightness = if lamp.Enabled then 3 else 0.5
            lamp.Color = if lamp.Enabled then warm else cold
            task.wait(0.1)
        end
        """;

    [Fact]
    public void A_lamp_flickers_on_the_ticks_its_script_waits_for_and_is_as_it_was_built_at_Stop()
    {
        using var level = new ScriptLevel();
        SceneNode lamp = level.Scene.Root.CreateChild("Lamp");
        lamp.LocalPosition = new Vector3(0f, 3f, 0f);
        Light light = lamp.Light = new Light { Kind = LightKind.Point, Intensity = 1.25f, Color = new Vector3(0.9f, 0.7f, 0.4f) };
        level.AddScript(lamp, Flicker);
        var before = new LevelState(level.Scene);
        int litWhenOn = Drawn(level).Length;
        Vector3 warm = Color3.FromRgb(255f, 180f, 80f).ToLinear();
        Vector3 cold = Color3.FromRgb(80f, 120f, 255f).ToLinear();

        level.Play();
        for (int tick = 1; tick <= 10 * TicksPerWait; tick++)
        {
            level.Tick();

            // The script runs at the head of the first tick, so the lamp is
            // off for the first six ticks, on for the next six, and so on.
            bool isOn = ((tick - 1) / TicksPerWait) % 2 == 1;
            light.Enabled.ShouldBe(isOn, $"tick {tick}");
            light.Intensity.ShouldBe(isOn ? 3f : 0.5f, $"tick {tick}");
            light.Color.ShouldBe(isOn ? warm : cold, $"tick {tick}");

            RenderLight[] drawn = Drawn(level);
            drawn.Length.ShouldBe(isOn ? litWhenOn : litWhenOn - 1, $"tick {tick}");
            if (isOn)
            {
                drawn.ShouldContain(
                    one => one.ColorIntensity == new Vector4(warm * 3f, (float)(int)RenderLightType.Point), $"tick {tick}");
            }
        }

        level.Errors.ShouldBeEmpty();
        new LevelState(level.Scene).IsSavedAs(before).ShouldBeFalse();

        level.Stop();

        new LevelState(level.Scene).ShouldBe(before, "after Stop");
        (light.Enabled, light.Intensity, light.Color).ShouldBe((true, 1.25f, new Vector3(0.9f, 0.7f, 0.4f)));
        Drawn(level).Length.ShouldBe(litWhenOn);
    }

    // The lights the next frame would draw, as the renderer is handed them.
    private static RenderLight[] Drawn(ScriptLevel level)
    {
        var view = new RenderView();
        level.Scene.BuildRenderView(level.Scene.Camera, view);
        return view.Lights.ToArray();
    }
}
