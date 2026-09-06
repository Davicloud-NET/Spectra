using Serilog;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The engine's own log on its way into the window.
/// </summary>
/// <remarks>
/// <b>What this closes is a diagnostic surface that reported to nobody.</b>
/// Serilog's three sinks leave the process; the shell's panel only ever showed
/// what the shell itself wrote. So a texture that would not decode, a material
/// naming a file that is not there, a map node that lost its mesh - all of them
/// went to a file, and the editor said nothing at all.
/// </remarks>
public sealed class EngineLogRelayTests
{
    /// <summary>A relay wired to a real Serilog logger, delivering inline.</summary>
    private static (EngineLogRelay Relay, ILogger Logger, List<EngineLogLine> Lines) Rig(
        bool attach = true)
    {
        var relay = new EngineLogRelay();
        var lines = new List<EngineLogLine>();
        relay.LineArrived += lines.Add;

        // Delivered inline: the poster is the seam that keeps this class free of
        // any UI type, and a test is allowed to be the thread.
        if (attach) relay.Attach(work => work());

        ILogger logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(relay)
            .CreateLogger();

        return (relay, logger, lines);
    }

    [Fact]
    public void A_warning_crosses_with_its_template_and_subject()
    {
        (_, ILogger logger, List<EngineLogLine> lines) = Rig();

        logger.Warning(
            "Material {Path}: texture {Texture} for '{Slot}' not found; using the placeholder",
            "Materials/wall.spectramat", "Textures/gone.png", "uDiffuse");

        EngineLogLine line = Assert.Single(lines);
        Assert.Equal(OutputSeverity.Warning, line.Severity);

        // The TEMPLATE, not the rendered text: it is what makes two reports
        // about one file a count and two reports about two files two rows.
        Assert.Contains("{Path}", line.Template);
        Assert.Equal("Materials/wall.spectramat", line.Subject);
        Assert.Contains("Textures/gone.png", line.Message);
        Assert.DoesNotContain("{", line.Message);
    }

    [Fact]
    public void An_error_crosses_as_an_error()
    {
        (_, ILogger logger, List<EngineLogLine> lines) = Rig();

        logger.Error("Renderer has no default shader");

        Assert.Equal(OutputSeverity.Error, Assert.Single(lines).Severity);
    }

    [Fact]
    public void Everything_quieter_than_a_warning_is_dropped()
    {
        (_, ILogger logger, List<EngineLogLine> lines) = Rig();

        // The engine logs at Information constantly - every texture, every
        // compile, the stats line every five seconds. Carrying that into the
        // panel would bury the two lines that matter.
        logger.Debug("Asset manager attached to {Backend} renderer", "D3D11");
        logger.Information("Loaded texture {Path} (128x128)", "Textures/wall.png");

        Assert.Empty(lines);
    }

    [Fact]
    public void A_resolution_line_crosses_even_though_it_is_information()
    {
        (_, ILogger logger, List<EngineLogLine> lines) = Rig();

        logger.Information(
            "Loaded texture {Path} after an earlier failure ({Description})",
            "Textures/wall.png", "128x128");

        EngineLogLine line = Assert.Single(lines);
        Assert.True(line.IsResolution);
        Assert.Equal("Textures/wall.png", line.Subject);
    }

    [Fact]
    public void An_exception_is_named_in_the_message()
    {
        (_, ILogger logger, List<EngineLogLine> lines) = Rig();

        logger.Error(new IOException("the file is locked"), "Could not read {Path}", "Maps/a.smap");

        string message = Assert.Single(lines).Message;
        Assert.Contains("IOException", message);
        Assert.Contains("the file is locked", message);
    }

    [Fact]
    public void A_line_with_no_path_carries_no_subject()
    {
        (_, ILogger logger, List<EngineLogLine> lines) = Rig();

        logger.Warning("The static world stopped rebuilding");

        Assert.Equal(string.Empty, Assert.Single(lines).Subject);
    }

    [Fact]
    public void A_burst_is_one_hop()
    {
        var relay = new EngineLogRelay();
        var lines = new List<EngineLogLine>();
        relay.LineArrived += lines.Add;

        // The poster is captured rather than run, so this counts the dispatcher
        // jobs a burst would have scheduled. A compile can warn once per frame;
        // one job per line would queue hundreds behind the frame producing them.
        var posted = new List<Action>();
        relay.Attach(posted.Add);

        ILogger logger = new LoggerConfiguration().WriteTo.Sink(relay).CreateLogger();
        for (int i = 0; i < 50; i++)
            logger.Warning("Material {Path} is unreadable", $"Materials/m{i}.spectramat");

        Assert.Single(posted);

        posted[0]();
        Assert.Equal(50, lines.Count);

        // In order, because a diagnostic read out of sequence is worse than one
        // read late.
        Assert.Contains("m0.spectramat", lines[0].Subject);
        Assert.Contains("m49.spectramat", lines[49].Subject);
    }

    [Fact]
    public void A_line_arriving_during_a_drain_schedules_another_hop()
    {
        var relay = new EngineLogRelay();
        var posted = new List<Action>();
        relay.Attach(posted.Add);

        ILogger logger = new LoggerConfiguration().WriteTo.Sink(relay).CreateLogger();
        logger.Warning("first");
        Assert.Single(posted);

        // The flag clears before the queue drains, so a line written while the
        // drain runs is not left sitting until something else happens to log.
        posted[0]();
        logger.Warning("second");
        Assert.Equal(2, posted.Count);
    }

    [Fact]
    public void Lines_logged_before_attach_are_delivered_on_attach()
    {
        var relay = new EngineLogRelay();
        var lines = new List<EngineLogLine>();
        relay.LineArrived += lines.Add;

        // The window does not exist when the logger is configured, and the lines
        // written in between are exactly the ones somebody needs when a session
        // opens wrong.
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(relay).CreateLogger();
        logger.Warning("Pack {Path} would not mount", "cooked/Demo.spack");
        Assert.Empty(lines);

        relay.Attach(work => work());
        Assert.Single(lines);
    }

    [Fact]
    public void Overflow_is_counted_and_reported_rather_than_silent()
    {
        var relay = new EngineLogRelay();
        var dropped = new List<int>();
        relay.LinesDropped += dropped.Add;

        ILogger logger = new LoggerConfiguration().WriteTo.Sink(relay).CreateLogger();
        for (int i = 0; i < EngineLogRelay.Capacity + 7; i++)
            logger.Warning("line {Index}", i);

        Assert.Equal(7, relay.Dropped);

        relay.Attach(work => work());
        Assert.Equal(7, Assert.Single(dropped));

        // And the counter resets, so the next overflow reports its own count
        // rather than a running total nobody can act on.
        Assert.Equal(0, relay.Dropped);
    }

    [Fact]
    public void Detaching_stops_delivery_and_queues_instead()
    {
        (EngineLogRelay relay, ILogger logger, List<EngineLogLine> lines) = Rig();

        logger.Warning("first");
        Assert.Single(lines);

        relay.Detach();
        logger.Warning("second");
        Assert.Single(lines);

        relay.Attach(work => work());
        Assert.Equal(2, lines.Count);
    }
}
