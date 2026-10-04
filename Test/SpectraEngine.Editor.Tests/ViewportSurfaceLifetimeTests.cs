using SpectraEngine.Editor.Viewport;

namespace SpectraEngine.Editor.Tests;

// A dock drag detaches and re-attaches the pane. Treated as a teardown, the
// re-attach would start a second session with a fresh scene and no history.
public sealed class ViewportSurfaceLifetimeTests
{
    [Fact]
    public void A_fresh_viewport_publishes_on_its_first_attach()
    {
        var lifetime = new ViewportSurfaceLifetime();

        lifetime.IsPublished.ShouldBeFalse();
        lifetime.Attached().ShouldBe(ViewportAttach.Publish);
        lifetime.IsPublished.ShouldBeTrue();
    }

    [Fact]
    public void A_detach_before_shutdown_ends_nothing()
    {
        var lifetime = new ViewportSurfaceLifetime();
        lifetime.Attached();

        lifetime.Detached().ShouldBeFalse();
        lifetime.IsPublished.ShouldBeTrue();
    }

    [Fact]
    public void A_re_attach_resumes_rather_than_publishing_a_second_surface()
    {
        var lifetime = new ViewportSurfaceLifetime();
        lifetime.Attached();
        lifetime.Detached();

        lifetime.Attached().ShouldBe(ViewportAttach.Resume);
        lifetime.IsPublished.ShouldBeTrue();
    }

    [Fact]
    public void Any_number_of_re_parents_publishes_exactly_once()
    {
        var lifetime = new ViewportSurfaceLifetime();

        lifetime.Attached().ShouldBe(ViewportAttach.Publish);

        for (int i = 0; i < 8; i++)
        {
            lifetime.Detached().ShouldBeFalse();
            lifetime.Attached().ShouldBe(ViewportAttach.Resume);
        }

        lifetime.Shutdown().ShouldBeTrue();
    }

    [Fact]
    public void Shutdown_owes_the_destroy_event_while_a_surface_is_live()
    {
        var lifetime = new ViewportSurfaceLifetime();
        lifetime.Attached();

        lifetime.Shutdown().ShouldBeTrue();
        lifetime.IsShuttingDown.ShouldBeTrue();
        lifetime.IsPublished.ShouldBeFalse();
    }

    [Fact]
    public void The_detach_after_a_shutdown_does_not_end_the_session_twice()
    {
        // The shell removes the control right after shutdown, so both run.
        var lifetime = new ViewportSurfaceLifetime();
        lifetime.Attached();
        lifetime.Shutdown().ShouldBeTrue();

        lifetime.Detached().ShouldBeFalse();
    }

    [Fact]
    public void A_viewport_that_never_published_owes_nothing_at_shutdown()
    {
        var lifetime = new ViewportSurfaceLifetime();

        lifetime.Shutdown().ShouldBeFalse();
        lifetime.IsShuttingDown.ShouldBeTrue();
    }

    [Fact]
    public void An_attach_after_shutdown_publishes_nothing()
    {
        // The compositor negotiation is async, so an attach can land late.
        var lifetime = new ViewportSurfaceLifetime();
        lifetime.Shutdown();

        lifetime.Attached().ShouldBe(ViewportAttach.Ignore);
        lifetime.IsPublished.ShouldBeFalse();
    }

    [Fact]
    public void Shutdown_is_idempotent()
    {
        var lifetime = new ViewportSurfaceLifetime();
        lifetime.Attached();

        lifetime.Shutdown().ShouldBeTrue();
        lifetime.Shutdown().ShouldBeFalse();
        lifetime.Shutdown().ShouldBeFalse();
    }
}
