namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The lines that join the sound preview to the engine's frame and to the
/// editor's window.
/// </summary>
// No test runs an Engine or a MainWindow, and nothing fails when one of these
// lines goes missing: a play button then does nothing, or shows stop after
// the viewport has restarted. So they are checked against the sources.
public sealed class SoundPreviewWiringConventionTests
{
    [Fact]
    public void The_engine_takes_a_hosts_request_before_the_device_updates_and_looks_at_the_preview_after()
    {
        string frame = Body(Engine(), "private void UpdateAudio(");

        int take = frame.IndexOf("_soundPreview.TakeRequest(Host);", StringComparison.Ordinal);
        int device = frame.IndexOf("_audioManager.Update();", StringComparison.Ordinal);
        int update = frame.IndexOf("_soundPreview.Update();", StringComparison.Ordinal);

        take.ShouldBeGreaterThanOrEqualTo(0);
        device.ShouldBeGreaterThan(take);
        update.ShouldBeGreaterThan(device, "a sound that ended is noticed once the device has let go of it");
    }

    [Fact]
    public void The_engine_publishes_what_plays_and_publishes_at_once_when_it_changes()
    {
        string publish = Body(Engine(), "private void PublishHostFrame(");

        publish.ShouldContain("if (_soundPreview.Path != _publishedPreview)");
        publish.ShouldContain("PreviewingSound = _publishedPreview = _soundPreview.Path,");
    }

    [Fact]
    public void The_engine_stops_the_preview_before_the_audio_device_closes()
    {
        string shutdown = Body(Engine(), "private void ShutdownSubsystems(");

        int stop = shutdown.IndexOf("_soundPreview.Stop();", StringComparison.Ordinal);
        int device = shutdown.IndexOf("_audioManager.Shutdown();", StringComparison.Ordinal);

        stop.ShouldBeGreaterThanOrEqualTo(0);
        device.ShouldBeGreaterThan(stop);
    }

    [Fact]
    public void The_window_sends_a_press_to_the_session_and_says_so_when_there_is_none()
    {
        string window = Window();

        window.ShouldContain("_shell.SoundPreview.Send = SendSoundPreview;");
        window.ShouldContain("_shell.SoundPreview.NotSent += () => _shell.SetWarning(SessionFaultText.ListenWhileStopped);");

        string send = Body(window, "private bool SendSoundPreview(");
        send.ShouldContain("if (_session is not { } session)");
        send.ShouldContain("session.PreviewSound(path);");
    }

    [Fact]
    public void A_session_that_ends_leaves_no_file_showing_stop()
    {
        Body(Window(), "private void StopSession(").ShouldContain("_shell.SoundPreview.EndSession();");
    }

    [Fact]
    public void The_status_bar_has_the_slot_that_stops_a_previewed_sound()
    {
        File.ReadAllText(Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml"))
            .ShouldContain("<shell:SoundPreviewStatus DataContext=\"{Binding SoundPreview}\"");
    }

    [Fact]
    public void A_sound_from_the_content_panel_is_placed_as_a_sound_and_not_as_a_model()
    {
        string place = Body(Window(), "private void PlaceAsset(");

        int sound = place.IndexOf("if (payload.Kind == ContentKind.Sound)", StringComparison.Ordinal);
        int insertSound = place.IndexOf("session.InsertSound(", StringComparison.Ordinal);
        int insertModel = place.IndexOf("session.InsertModel(", StringComparison.Ordinal);

        sound.ShouldBeGreaterThanOrEqualTo(0);
        insertSound.ShouldBeGreaterThan(sound);
        insertModel.ShouldBeGreaterThan(insertSound);
    }

    // A method's text, from its signature to the brace that closes it. Split
    // on "\n" and trimmed: a checkout can be LF or CRLF.
    private static string Body(string source, string signature)
    {
        string[] lines = source.Split('\n');
        int start = Array.FindIndex(lines, line => line.Contains(signature, StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, $"no method starts with '{signature}'");

        // A member's closing brace sits at the member's own indent.
        string indent = lines[start][..(lines[start].Length - lines[start].TrimStart().Length)];
        int end = Array.FindIndex(lines, start + 1, line => line.TrimEnd() == indent + "}");
        end.ShouldBeGreaterThan(start, $"'{signature}' never closes");

        return string.Join('\n', lines[start..(end + 1)].Select(line => line.Trim()));
    }

    private static string Engine() => File.ReadAllText(
        Path.Combine(SourceRoot(), "SpectraEngine.Core", "Engine.cs"));

    private static string Window() => File.ReadAllText(
        Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml.cs"));

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the solution root above the test binary");
    }
}
