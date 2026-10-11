using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The slot a host installs a script runtime in, and what a level is told
/// when its scripts cannot run. None of this needs the Luau library.
/// </summary>
public sealed class ScriptHostingTests
{
    private const string LibraryMissing =
        "The Luau library was not found. Build it with native/build-luau.ps1 on Windows " +
        "or native/build-luau.sh on Linux.";

    [Fact]
    public void With_nothing_installed_a_level_with_scripts_plays_without_them_and_says_so_once()
    {
        var hosting = new ScriptHosting();
        var logger = new CapturingLogger();

        hosting.CreateRuntime(LevelWithScripts(2), null, logger).ShouldBeNull();
        hosting.CreateRuntime(LevelWithScripts(2), null, logger).ShouldBeNull();

        string said = logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        said.ShouldContain("2 script(s)");
    }

    [Fact]
    public void A_runtime_that_cannot_be_made_is_said_once_with_the_script_that_builds_the_library()
    {
        var hosting = new ScriptHosting { RuntimeFactory = _ => throw new InvalidOperationException(LibraryMissing) };
        var logger = new CapturingLogger();

        hosting.CreateRuntime(LevelWithScripts(1), null, logger).ShouldBeNull();
        hosting.CreateRuntime(LevelWithScripts(1), null, logger).ShouldBeNull();

        string said = logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        said.ShouldContain("native/build-luau.ps1");
        said.ShouldContain("1 script(s)");
    }

    // What a library built for another kind of processor throws when it loads.
    [Fact]
    public void A_factory_that_fails_some_other_way_still_lets_the_level_play_without_its_scripts()
    {
        var hosting = new ScriptHosting { RuntimeFactory = _ => throw new BadImageFormatException("not for this machine") };
        var logger = new CapturingLogger();

        hosting.CreateRuntime(LevelWithScripts(1), null, logger).ShouldBeNull();
        hosting.CreateRuntime(LevelWithScripts(1), null, logger).ShouldBeNull();

        string said = logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        said.ShouldContain("1 script(s)");
        said.ShouldContain("not for this machine");
    }

    [Fact]
    public void A_level_with_no_script_asks_for_no_runtime_and_says_nothing()
    {
        int asked = 0;
        var hosting = new ScriptHosting
        {
            RuntimeFactory = _ =>
            {
                asked++;
                throw new InvalidOperationException(LibraryMissing);
            },
        };
        var logger = new CapturingLogger();

        hosting.CreateRuntime(LevelWithScripts(0), null, logger).ShouldBeNull();

        asked.ShouldBe(0);
        logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void The_factory_is_handed_the_level_the_sink_and_where_scripts_are_read()
    {
        var sink = new RecordingSink();
        var source = new TextScriptSource();
        ScriptRuntimeSetup? handed = null;
        var hosting = new ScriptHosting
        {
            Sink = sink,
            Source = source,
            RuntimeFactory = setup =>
            {
                handed = setup;
                throw new InvalidOperationException("not this time");
            },
        };
        EntityWorld level = LevelWithScripts(1);

        hosting.CreateRuntime(level, null, NullLogger.Instance);

        ScriptRuntimeSetup setup = handed.ShouldNotBeNull();
        setup.World.ShouldBeSameAs(level);
        setup.Sink.ShouldBeSameAs(sink);
        setup.Source.ShouldBeSameAs(source);
    }

    private static EntityWorld LevelWithScripts(int count)
    {
        var scene = new Scene("Level");
        for (int i = 0; i < count; i++)
            scene.Root.CreateChild($"Script{i}").Script = new Script("Scripts/any.luau");

        return new EntityWorld(scene, NullLogger.Instance, new EntityCatalog());
    }
}
