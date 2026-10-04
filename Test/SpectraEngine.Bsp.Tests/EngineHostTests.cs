using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="EngineHost"/>: queued commands, request latches, frame snapshots
/// and the scene change log.
/// </summary>
public sealed class EngineHostTests
{
    private static EngineHost NewHost() => new(NullLogger.Instance);

    [Fact]
    public void A_queued_command_does_not_run_until_the_engine_drains_it()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        bool ran = false;

        host.EnqueueCommand(_ => ran = true);

        ran.ShouldBeFalse();
        host.PendingCommandCount.ShouldBe(1);

        host.DrainCommands(scene);

        ran.ShouldBeTrue();
        host.PendingCommandCount.ShouldBe(0);
    }

    [Fact]
    public void A_command_receives_the_scene_that_is_live_when_it_runs()
    {
        var first = new Scene("First");
        var second = new Scene("Second");
        EngineHost host = NewHost();

        Scene? seen = null;
        host.EnqueueCommand(s => seen = s);
        host.DrainCommands(second);

        seen.ShouldBeSameAs(second);
        seen.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void Commands_queued_with_no_scene_are_held_rather_than_dropped()
    {
        // No scene yet, as during a load.
        EngineHost host = NewHost();
        int ran = 0;
        host.EnqueueCommand(_ => ran++);

        host.DrainCommands(scene: null);

        ran.ShouldBe(0);
        host.PendingCommandCount.ShouldBe(1);

        host.DrainCommands(new Scene("Late"));
        ran.ShouldBe(1);
    }

    [Fact]
    public void A_command_that_throws_is_logged_and_the_frame_continues()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        bool afterRan = false;

        host.EnqueueCommand(_ => throw new InvalidOperationException("shell bug"));
        host.EnqueueCommand(_ => afterRan = true);

        Should.NotThrow(() => host.DrainCommands(scene));
        afterRan.ShouldBeTrue();
    }

    [Fact]
    public void A_flood_of_commands_cannot_starve_the_frame()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        for (int i = 0; i < 500; i++)
            host.EnqueueCommand(_ => { });

        host.DrainCommands(scene, maxPerFrame: 100);

        host.PendingCommandCount.ShouldBe(400);
    }

    [Fact]
    public async Task Commands_may_be_queued_from_another_thread_while_the_engine_drains()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        int ran = 0;
        const int Total = 2000;

        Task producer = Task.Run(() =>
        {
            for (int i = 0; i < Total; i++)
                host.EnqueueCommand(_ => Interlocked.Increment(ref ran));
        });

        while (!producer.IsCompleted || host.PendingCommandCount > 0)
            host.DrainCommands(scene);

        await producer;
        host.DrainCommands(scene);

        ran.ShouldBe(Total);
    }

    [Fact]
    public void Requesting_shutdown_is_visible_and_idempotent()
    {
        EngineHost host = NewHost();
        host.ShutdownRequested.ShouldBeFalse();

        host.RequestShutdown();
        host.RequestShutdown();

        host.ShutdownRequested.ShouldBeTrue();
    }

    [Fact]
    public void A_play_mode_request_is_latched_until_the_engine_takes_it()
    {
        EngineHost host = NewHost();

        host.TryTakePlayModeRequest(out _).ShouldBeFalse("nothing was requested");

        host.RequestPlayMode(true);

        host.TryTakePlayModeRequest(out bool enter).ShouldBeTrue();
        enter.ShouldBeTrue();

        // Taken once, or play mode is re-entered every frame.
        host.TryTakePlayModeRequest(out _).ShouldBeFalse();
    }

    [Fact]
    public void The_newest_play_mode_request_wins()
    {
        EngineHost host = NewHost();

        host.RequestPlayMode(true);
        host.RequestPlayMode(false);

        host.TryTakePlayModeRequest(out bool enter).ShouldBeTrue();
        enter.ShouldBeFalse();
    }

    [Fact]
    public void Debug_visualisation_requests_accumulate_until_taken()
    {
        EngineHost host = NewHost();

        host.RequestDebugVisualization(DebugVisualization.Wireframe, enabled: true);
        host.RequestDebugVisualization(DebugVisualization.Aabbs, enabled: true);
        host.RequestDebugVisualization(DebugVisualization.Normals, enabled: false);

        host.TakeDebugVisualizationRequests(out DebugVisualization set, out DebugVisualization clear);
        set.ShouldBe(DebugVisualization.Wireframe | DebugVisualization.Aabbs);
        clear.ShouldBe(DebugVisualization.Normals);

        host.TakeDebugVisualizationRequests(out set, out clear);
        set.ShouldBe(DebugVisualization.None);
        clear.ShouldBe(DebugVisualization.None);
    }

    [Fact]
    public void The_newest_debug_visualisation_request_wins_per_flag()
    {
        // On then off in one frame must not leave the flag in both masks.
        EngineHost host = NewHost();

        host.RequestDebugVisualization(DebugVisualization.Wireframe, enabled: true);
        host.RequestDebugVisualization(DebugVisualization.Wireframe, enabled: false);

        host.TakeDebugVisualizationRequests(out DebugVisualization set, out DebugVisualization clear);
        set.ShouldBe(DebugVisualization.None);
        clear.ShouldBe(DebugVisualization.Wireframe);
    }

    [Fact]
    public void A_pipeline_request_is_taken_once_and_the_newest_wins()
    {
        EngineHost host = NewHost();

        host.TakeRequestedPipeline().ShouldBeNull();

        host.RequestPipeline("Forward");
        host.RequestPipeline("Deferred");

        host.TakeRequestedPipeline().ShouldBe("Deferred");
        host.TakeRequestedPipeline().ShouldBeNull();
    }

    [Fact]
    public void A_blank_pipeline_name_is_refused_at_the_boundary()
    {
        EngineHost host = NewHost();

        Should.Throw<ArgumentException>(() => host.RequestPipeline(""));
        Should.Throw<ArgumentException>(() => host.RequestPipeline("   "));
    }

    [Fact]
    public void A_snapshot_is_published_on_an_interval_not_every_frame()
    {
        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.FromMilliseconds(100);

        var published = new List<FrameSnapshot>();
        host.FrameCompleted += published.Add;

        // Ten frames inside one interval.
        for (int i = 0; i < 10; i++)
            host.PublishFrame(TimeSpan.FromMilliseconds(i), Build);

        published.Count.ShouldBe(1);

        host.PublishFrame(TimeSpan.FromMilliseconds(500), Build);
        published.Count.ShouldBe(2);
    }

    [Fact]
    public void Structural_news_does_not_wait_for_the_interval()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.FromSeconds(10);
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build); // clears the scene-swap overflow

        var published = new List<FrameSnapshot>();
        host.FrameCompleted += published.Add;

        host.PublishFrame(TimeSpan.FromMilliseconds(1), Build).ShouldBeNull();
        published.ShouldBeEmpty();

        scene.Root.CreateChild("New");
        host.PublishFrame(TimeSpan.FromMilliseconds(2), Build).ShouldNotBeNull();
        published.Count.ShouldBe(1);
        published[0].Changes.Count.ShouldBe(1);
        published[0].Changes[0].Kind.ShouldBe(SceneChangeKind.Added);
    }

    [Fact]
    public void The_last_snapshot_is_available_to_a_shell_that_attaches_late()
    {
        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.LastSnapshot.ShouldBeSameAs(FrameSnapshot.Empty);

        host.PublishFrame(TimeSpan.FromMilliseconds(1), Build);

        host.LastSnapshot.ShouldNotBeSameAs(FrameSnapshot.Empty);
        host.LastSnapshot.FrameNumber.ShouldBe(1);
    }

    [Fact]
    public void A_published_snapshot_never_changes_afterwards()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);

        scene.Root.CreateChild("A");
        FrameSnapshot first = host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!;
        int countAtPublish = first.Changes.Count;

        scene.Root.CreateChild("B");
        scene.Root.CreateChild("C");
        host.PublishFrame(TimeSpan.FromMilliseconds(2), Build);

        first.Changes.Count.ShouldBe(countAtPublish);
    }

    [Fact]
    public void A_reparent_is_reported_even_though_it_raises_no_membership_event()
    {
        var scene = new Scene("Host");
        SceneNode a = scene.Root.CreateChild("A");
        SceneNode b = scene.Root.CreateChild("B");

        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build);

        a.AddChild(b);

        FrameSnapshot snapshot = host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!;
        snapshot.Changes.Count.ShouldBe(1);

        SceneChange change = snapshot.Changes[0];
        change.Kind.ShouldBe(SceneChangeKind.Reparented);
        change.NodeId.ShouldBe(b.Id);
        change.ParentId.ShouldBe(a.Id);
        change.SiblingIndex.ShouldBe(0);
    }

    [Fact]
    public void A_reorder_under_the_same_parent_is_reported_too()
    {
        // Sibling order is the static world's placement order.
        var scene = new Scene("Host");
        scene.Root.CreateChild("A");
        SceneNode b = scene.Root.CreateChild("B");

        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build);

        scene.Root.InsertChild(0, b);

        FrameSnapshot snapshot = host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!;
        snapshot.Changes.Count.ShouldBe(1);
        snapshot.Changes[0].Kind.ShouldBe(SceneChangeKind.Reparented);
        snapshot.Changes[0].SiblingIndex.ShouldBe(0);
    }

    [Fact]
    public void A_rename_is_reported_even_though_nothing_structural_moved()
    {
        var scene = new Scene("Host");
        SceneNode node = scene.Root.CreateChild("Before");

        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build);

        node.Name = "After";

        FrameSnapshot snapshot = host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!;
        snapshot.Changes.Count.ShouldBe(1);

        SceneChange change = snapshot.Changes[0];
        change.Kind.ShouldBe(SceneChangeKind.Renamed);
        change.NodeId.ShouldBe(node.Id);
        change.Name.ShouldBe("After");
    }

    [Fact]
    public void Writing_the_name_a_node_already_has_reports_nothing()
    {
        // Redo replays the same value; it must stay out of the log.
        var scene = new Scene("Host");
        SceneNode node = scene.Root.CreateChild("Same");

        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build);

        node.Name = "Same";

        host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!.Changes.ShouldBeEmpty();
    }

    [Fact]
    public void An_attached_subtree_is_reported_once_per_node_parents_first()
    {
        var scene = new Scene("Host");
        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build);

        var group = new SceneNode("Group");
        SceneNode child = group.CreateChild("Child");
        child.CreateChild("Grandchild");
        scene.Root.AddChild(group);

        FrameSnapshot snapshot = host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!;
        snapshot.Changes.Count.ShouldBe(3);
        snapshot.Changes[0].Name.ShouldBe("Group");
        snapshot.Changes[1].Name.ShouldBe("Child");
        snapshot.Changes[2].Name.ShouldBe("Grandchild");
        snapshot.ChangesOverflowed.ShouldBeFalse();
    }

    [Fact]
    public void Transform_changes_are_not_logged()
    {
        var scene = new Scene("Host");
        SceneNode node = scene.Root.CreateChild("Mover");

        EngineHost host = NewHost();
        host.SnapshotInterval = TimeSpan.Zero;
        host.ObserveScene(scene);
        host.PublishFrame(TimeSpan.Zero, Build);

        for (int i = 1; i <= 100; i++)
            node.LocalPosition = new Vector3(i, 0f, 0f);

        host.PublishFrame(TimeSpan.FromMilliseconds(1), Build)!.Changes.ShouldBeEmpty();
    }

    [Fact]
    public void An_overflowing_log_says_so_instead_of_truncating_silently()
    {
        var scene = new Scene("Host");
        var log = new SceneChangeLog(capacity: 8);
        log.Observe(scene);
        log.Drain(); // clear the scene-swap overflow

        for (int i = 0; i < 20; i++)
            scene.Root.CreateChild($"N{i}");

        (IReadOnlyList<SceneChange> changes, bool overflowed) = log.Drain();
        changes.Count.ShouldBe(8);
        overflowed.ShouldBeTrue();

        // The flag clears with the batch.
        log.Drain().Overflowed.ShouldBeFalse();
    }

    [Fact]
    public void Swapping_the_observed_scene_reports_an_overflow_rather_than_a_fake_diff()
    {
        var first = new Scene("First");
        var log = new SceneChangeLog();
        log.Observe(first);
        log.Drain();

        first.Root.CreateChild("Doomed");
        log.Observe(new Scene("Second"));

        (IReadOnlyList<SceneChange> changes, bool overflowed) = log.Drain();
        changes.ShouldBeEmpty();
        overflowed.ShouldBeTrue();
    }

    [Fact]
    public void A_detached_scene_stops_being_reported()
    {
        var scene = new Scene("Host");
        var log = new SceneChangeLog();
        log.Observe(scene);
        log.Observe(null);
        log.Drain();

        scene.Root.CreateChild("Ignored");

        log.Drain().Changes.ShouldBeEmpty();
    }

    [Fact]
    public void Submitted_input_reaches_the_engine_state_immediately()
    {
        // Not queued: InputManager is already lock-guarded, and a queue would
        // only add a frame of latency.
        var input = new InputManager(NullLogger<InputManager>.Instance);
        EngineHost host = NewHost();
        host.AttachInput(input);

        host.SubmitInput(InputEvent.KeyDown(InputKey.W));

        input.IsKeyDown(InputKey.W).ShouldBeTrue();
    }

    [Fact]
    public void Submitting_input_before_an_engine_exists_is_ignored_rather_than_fatal()
    {
        // A shell can wire up viewport events before it starts the engine.
        EngineHost host = NewHost();

        Should.NotThrow(() => host.SubmitInput(InputEvent.KeyDown(InputKey.W)));
        host.RequestedCursorMode.ShouldBe(CursorMode.Normal);
        Should.NotThrow(host.ApplyPendingCursorMode);
    }

    [Fact]
    public void A_hosts_cursor_request_is_visible_before_it_is_applied()
    {
        // Embedded: the engine asks, the shell does the platform capture and
        // then acknowledges.
        var input = new InputManager(NullLogger<InputManager>.Instance);
        EngineHost host = NewHost();
        host.AttachInput(input);

        input.RequestCursorMode(CursorMode.Locked);

        host.RequestedCursorMode.ShouldBe(CursorMode.Locked);
        input.CursorMode.ShouldBe(CursorMode.Normal, "nothing has applied it yet");

        host.ApplyPendingCursorMode();

        input.CursorMode.ShouldBe(CursorMode.Locked);
        input.IsCursorLocked.ShouldBeTrue();
    }

    private static FrameSnapshot Build(FrameSnapshotBuilder builder) => new()
    {
        FrameNumber = builder.FrameNumber,
        Changes = builder.Changes,
        ChangesOverflowed = builder.ChangesOverflowed,
    };
}
