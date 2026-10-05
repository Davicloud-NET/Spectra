using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Acoustics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="MaterialAcoustics"/>: from a <see cref="MaterialRef"/> to its
/// preset through the content sources, with no renderer anywhere.
/// </summary>
public sealed class MaterialAcousticsTests
{
    private readonly RecordingContentSource _source = new();
    private readonly CapturingLogger _logger = new();
    private readonly MaterialAcoustics _acoustics;

    public MaterialAcousticsTests()
    {
        var stack = new ContentSourceStack();
        stack.Mount(_source);
        _acoustics = new MaterialAcoustics(_logger, stack);
    }

    [Fact]
    public void A_material_resolves_to_the_preset_its_file_names()
    {
        MaterialRef door = Author("door", "shader = lit\nacoustic = wood\n");

        _acoustics.Resolve(door).ShouldBeSameAs(AcousticPresets.Wood);
        _logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(_logger.Describe());
    }

    [Fact]
    public void The_probe_and_the_open_ask_the_same_source_for_the_same_path()
    {
        // Interned the way a hand-written map might spell it.
        string path = PathOf("slashes");
        _source.Add(path, "acoustic = glass");
        MaterialRef pane = MaterialRegistry.Intern(@"Materials\.\acoustics-tests\slashes.spectramat");

        _acoustics.Resolve(pane).ShouldBeSameAs(AcousticPresets.Glass);

        _source.Probed.ShouldHaveSingleItem().ShouldBe(path);
        _source.Opened.ShouldHaveSingleItem().ShouldBe(path);
    }

    [Fact]
    public void A_second_ask_does_not_read_the_file_again()
    {
        MaterialRef wall = Author("cached", "acoustic = brick");

        _acoustics.Resolve(wall);
        _acoustics.Resolve(wall).ShouldBeSameAs(AcousticPresets.Brick);
        _acoustics.Resolve(wall);

        _source.Opened.Count.ShouldBe(1);
        _source.Probed.Count.ShouldBe(1);
    }

    [Fact]
    public void A_material_without_the_line_is_generic_and_nothing_is_said()
    {
        MaterialRef plain = Author("plain", "shader = lit\nfloat uRoughness = 0.5\n");

        _acoustics.Resolve(plain).ShouldBeSameAs(AcousticPresets.Generic);
        _logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(_logger.Describe());
    }

    [Fact]
    public void A_missing_material_is_generic_and_says_so_once()
    {
        MaterialRef missing = MaterialRegistry.Intern(PathOf("missing"));

        for (int i = 0; i < 3; i++)
            _acoustics.Resolve(missing).ShouldBeSameAs(AcousticPresets.Generic);

        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain(PathOf("missing"));
        warning.ShouldContain("not found");
        _source.Opened.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_that_is_not_a_material_is_generic_and_says_so_once()
    {
        MaterialRef junk = Author("junk", "PNG\r\n\u001a\nIHDR and nothing a parser wants\n");

        for (int i = 0; i < 3; i++)
            _acoustics.Resolve(junk).ShouldBeSameAs(AcousticPresets.Generic);

        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain(PathOf("junk"));
        warning.ShouldContain("no line the parser could use");
    }

    [Fact]
    public void A_file_its_source_refuses_is_generic_and_says_so_once()
    {
        string path = PathOf("refused");
        _source.Refuse(path, "the cook would not have it");
        MaterialRef refused = MaterialRegistry.Intern(path);

        for (int i = 0; i < 3; i++)
            _acoustics.Resolve(refused).ShouldBeSameAs(AcousticPresets.Generic);

        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain(path);
        warning.ShouldContain("the cook would not have it");
        _source.Opened.Count.ShouldBe(1);
    }

    [Fact]
    public void A_path_that_leaves_the_content_root_is_generic_and_says_so_once()
    {
        MaterialRef outside = MaterialRegistry.Intern("../acoustics-tests/outside.spectramat");

        for (int i = 0; i < 3; i++)
            _acoustics.Resolve(outside).ShouldBeSameAs(AcousticPresets.Generic);

        _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain("not a usable path");
        _source.Probed.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_preset_name_is_generic()
    {
        MaterialRef typo = Author("typo", "shader = lit\nacoustic = wod\n");

        _acoustics.Resolve(typo).ShouldBeSameAs(AcousticPresets.Generic);
    }

    [Fact]
    public void A_face_with_no_material_is_generic_and_touches_no_content()
    {
        _acoustics.Resolve(MaterialRef.Default).ShouldBeSameAs(AcousticPresets.Generic);

        _source.Probed.ShouldBeEmpty();
        _source.Opened.ShouldBeEmpty();
        _logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(_logger.Describe());
    }

    [Fact]
    public void An_answer_already_given_allocates_nothing()
    {
        MaterialRef wall = Author("steady", "acoustic = concrete");
        MaterialRef missing = MaterialRegistry.Intern(PathOf("steady-missing"));
        _acoustics.Resolve(wall);
        _acoustics.Resolve(missing);

        // The least of several rounds: a one-off from the runtime is not a cost per ask.
        long least = long.MaxValue;
        int concrete = 0;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                if (ReferenceEquals(_acoustics.Resolve(wall), AcousticPresets.Concrete)) concrete++;
                _acoustics.Resolve(missing);
                _acoustics.Resolve(MaterialRef.Default);
            }

            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        concrete.ShouldBe(5000);
    }

    [Fact]
    public void A_forgotten_material_is_read_again()
    {
        string path = PathOf("late");
        MaterialRef late = MaterialRegistry.Intern(path);

        _acoustics.Resolve(late).ShouldBeSameAs(AcousticPresets.Generic);
        _acoustics.Forget(MaterialRef.Default).ShouldBeFalse();

        // The file arrives after the first ask, as when the editor saves one.
        _source.Add(path, "acoustic = metal");
        _acoustics.Resolve(late).ShouldBeSameAs(AcousticPresets.Generic);

        _acoustics.Forget(late).ShouldBeTrue();
        _acoustics.Forget(late).ShouldBeFalse();
        _acoustics.Resolve(late).ShouldBeSameAs(AcousticPresets.Metal);
    }

    [Fact]
    public void Forgetting_a_file_drops_the_answer_of_every_spelling_of_its_path()
    {
        string path = PathOf("late-spelled");
        MaterialRef plain = MaterialRegistry.Intern(path);
        MaterialRef dotted = MaterialRegistry.Intern(@".\Materials\\acoustics-tests\late-spelled.spectramat");
        dotted.ShouldNotBe(plain);

        _acoustics.Resolve(plain).ShouldBeSameAs(AcousticPresets.Generic);
        _acoustics.Resolve(dotted).ShouldBeSameAs(AcousticPresets.Generic);
        _acoustics.Forget(PathOf("never-asked")).ShouldBeFalse();

        _source.Add(path, "acoustic = metal");
        _acoustics.Forget(path).ShouldBeTrue();

        _acoustics.Resolve(plain).ShouldBeSameAs(AcousticPresets.Metal);
        _acoustics.Resolve(dotted).ShouldBeSameAs(AcousticPresets.Metal);
    }

    [Fact]
    public void Many_threads_asking_at_once_read_the_file_once_and_warn_once()
    {
        MaterialRef wall = Author("crowd", "acoustic = rock");
        MaterialRef missing = MaterialRegistry.Intern(PathOf("crowd-missing"));

        // Real threads, so the barrier always gets everyone it waits for.
        const int count = 8;
        using var start = new Barrier(count);
        var answers = new AcousticPreset?[count * 2];
        var threads = new Thread[count];

        for (int i = 0; i < count; i++)
        {
            int slot = i * 2;
            threads[i] = new Thread(() =>
            {
                start.SignalAndWait();
                answers[slot] = _acoustics.Resolve(wall);
                answers[slot + 1] = _acoustics.Resolve(missing);
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads)
            thread.Join(TimeSpan.FromSeconds(30)).ShouldBeTrue("a thread did not come back");

        for (int i = 0; i < count; i++)
        {
            answers[i * 2].ShouldBeSameAs(AcousticPresets.Rock);
            answers[i * 2 + 1].ShouldBeSameAs(AcousticPresets.Generic);
        }

        _source.Opened.Count.ShouldBe(1);
        _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain(PathOf("crowd-missing"));
    }

    [Fact]
    public void The_demo_materials_name_what_they_are_made_of()
    {
        var content = new ContentSourceStack();
        content.Mount(new LooseFileSource(NullLogger.Instance, ContentRoot.Path));
        var acoustics = new MaterialAcoustics(_logger, content);

        Demo(acoustics, "wall").ShouldBeSameAs(AcousticPresets.Brick);
        Demo(acoustics, "PostWood").ShouldBeSameAs(AcousticPresets.Wood);
        Demo(acoustics, "pbr_gold").ShouldBeSameAs(AcousticPresets.Metal);
        Demo(acoustics, "pbr_copper_brushed").ShouldBeSameAs(AcousticPresets.Metal);

        // A development texture is made of nothing in particular.
        Demo(acoustics, "dev_grid").ShouldBeSameAs(AcousticPresets.Generic);

        _logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(_logger.Describe());
    }

    private static AcousticPreset Demo(MaterialAcoustics acoustics, string name) =>
        acoustics.Resolve(MaterialRegistry.Intern($"Materials/{name}{MaterialParser.FileExtension}"));

    private MaterialRef Author(string name, string text)
    {
        _source.Add(PathOf(name), text);
        return MaterialRegistry.Intern(PathOf(name));
    }

    // The registry is shared by the whole process, so these stay out of every other test's way.
    private static string PathOf(string name) => $"Materials/acoustics-tests/{name}{MaterialParser.FileExtension}";
}
