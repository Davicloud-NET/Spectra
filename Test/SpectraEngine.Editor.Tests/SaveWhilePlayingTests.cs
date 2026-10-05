using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The session's own refusal to save a level that is playing. The shell asks
/// first, from a snapshot that can be a frame old.
/// </summary>
public sealed class SaveWhilePlayingTests
{
    [Fact]
    public void A_save_is_refused_while_entities_run()
    {
        var entities = new EntityWorld(new Scene("Level"), NullLogger.Instance, new EntityCatalog());
        entities.Activate();

        InvalidOperationException refusal = EditorSession.SaveRefusal(entities).ShouldNotBeNull();

        refusal.Message.ShouldContain("playing");
        refusal.Message.ShouldContain("Stop");
    }

    [Fact]
    public void A_save_goes_ahead_once_the_entities_have_stopped()
    {
        var entities = new EntityWorld(new Scene("Level"), NullLogger.Instance, new EntityCatalog());
        entities.Activate();
        entities.Deactivate();

        EditorSession.SaveRefusal(entities).ShouldBeNull();
    }

    [Fact]
    public void A_save_goes_ahead_when_nothing_is_playing()
    {
        EditorSession.SaveRefusal(null).ShouldBeNull();
    }
}
