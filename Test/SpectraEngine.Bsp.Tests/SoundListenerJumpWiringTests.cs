namespace SpectraEngine.Bsp.Tests;

// The engine loop needs a window, so no test runs it. Without the one call
// that passes a teleport on, every sound chirps at a short one and nothing
// else fails. So that call is checked in the source text.
public sealed class SoundListenerJumpWiringTests
{
    [Fact]
    public void The_engine_tells_the_presenter_of_a_view_jump_before_it_updates_the_sounds()
    {
        string engine = File.ReadAllText(Path.Combine(SourceRoot(), "SpectraEngine.Core", "Engine.cs"));

        int taken = engine.IndexOf(".TryTakeViewJump()", StringComparison.Ordinal);
        int told = engine.IndexOf("_soundPresenter.ListenerJumped()", StringComparison.Ordinal);
        int updated = engine.IndexOf("_soundPresenter.Update(", StringComparison.Ordinal);

        taken.ShouldBeGreaterThan(-1, "the engine has to ask the first-person view whether it jumped");
        told.ShouldBeGreaterThan(taken, "and tell the sound presenter when it did");
        updated.ShouldBeGreaterThan(told, "before the presenter reads the listener for the frame");
    }

    // Repo root: the nearest ancestor holding a solution file.
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file above {AppContext.BaseDirectory}; the source test needs the repo.");
    }
}
