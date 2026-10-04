using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Editor.Viewport;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The composited viewport's frame pump: which generation is on screen, when a
/// superseded one may be released, and what the renderer is told about it.
/// </summary>
// The fakes complete their tasks on the test's thread, so continuations run
// inline and the self-rescheduling loop can be stepped.
public sealed class CompositedFramePumpTests
{
    private const nint ProducerHandle = 0x1000;
    private const int Width = 320;
    private const int Height = 240;

    private sealed class FakeImage : ICompositedImage
    {
        // No RunContinuationsAsynchronously: continuations must run inline on
        // the test's thread.
        private readonly TaskCompletionSource _import = new();

        // A queue: the pump keeps HandOverDepth hand-overs outstanding. A
        // single slot would orphan the earlier task. Completed oldest first,
        // like the compositor's server jobs.
        private readonly Queue<TaskCompletionSource> _updates = new();

        internal int Updates { get; private set; }

        // Set by FakeSource so an update can record where it was issued from.
        internal Func<bool>? InsideResume { get; set; }

        // One entry per update: was it issued from inside a resume?
        internal List<bool> UpdateSites { get; } = [];

        internal bool Disposed { get; private set; }

        internal uint LastAcquireKey { get; private set; }

        internal uint LastReleaseKey { get; private set; }

        internal bool UpdateInFlight => _updates.Count > 0;

        internal int UpdatesInFlight => _updates.Count;

        public Task ImportCompleted => _import.Task;

        public Task UpdateAsync(uint acquireKey, uint releaseKey)
        {
            Updates++;
            UpdateSites.Add(InsideResume?.Invoke() ?? true);
            LastAcquireKey = acquireKey;
            LastReleaseKey = releaseKey;
            var update = new TaskCompletionSource();
            _updates.Enqueue(update);
            return update.Task;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        internal void CompleteImport() => _import.TrySetResult();

        internal void FailImport() => _import.TrySetException(new InvalidOperationException("refused"));

        internal void CompleteUpdate()
        {
            if (_updates.TryDequeue(out TaskCompletionSource? update))
                update.TrySetResult();
        }

        internal void CompleteAllUpdates()
        {
            // Bounded: completing one re-issues another inline, so draining to
            // empty never terminates.
            for (int outstanding = _updates.Count; outstanding > 0; outstanding--)
                CompleteUpdate();
        }

        internal void FailUpdate()
        {
            if (_updates.TryDequeue(out TaskCompletionSource? update))
                update.TrySetException(new InvalidOperationException("hand-over refused"));
        }
    }

    private sealed class FakeSource : ICompositedImageSource
    {
        internal Func<bool>? InsideResume { get; set; }

        internal List<FakeImage> Images { get; } = [];

        internal List<nint> ImportedHandles { get; } = [];

        internal FakeImage Latest => Images[^1];

        // The real one disposes the drawing surface every import snapshots into.
        internal bool Disposed { get; private set; }

        public ICompositedImage Import(nint ntHandle, int width, int height)
        {
            ImportedHandles.Add(ntHandle);
            var image = new FakeImage { InsideResume = InsideResume };
            Images.Add(image);
            return image;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Rig
    {
        // True while a posted action runs. Stands in for "on the UI thread".
        internal bool InsideResume { get; private set; }

        internal FakeSource Source { get; } = new();

        internal List<int> Acknowledged { get; } = [];

        internal List<nint> Closed { get; } = [];

        internal int Resumes { get; private set; }

        internal int Faults { get; private set; }

        // Holds resumes instead of running them: the hand-over has finished
        // but the loop is not back on the UI thread yet.
        internal bool HoldResumes { get; init; }

        internal int PendingResumes => _held.Count;

        internal void ReleaseResumes()
        {
            // A released resume can queue another behind it.
            while (_held.Count > 0)
                _held.Dequeue()();
        }

        private readonly Queue<Action> _held = new();

        internal CompositedFramePump Pump { get; }

        internal Rig()
        {
            // The duplicate is a distinct value so tests can tell it from the
            // producer's handle.
            Pump = new CompositedFramePump(
                Source,
                Acknowledged.Add,
                NullLogger.Instance,
                handle => handle + 0x10000,
                Closed.Add,
                onFault: () => Faults++,

                // Inline: no dispatcher runs here to drain a post.
                resumeOnUiThread: action =>
                {
                    Resumes++;
                    if (HoldResumes)
                    {
                        _held.Enqueue(action);
                        return;
                    }

                    InsideResume = true;
                    try
                    {
                        action();
                    }
                    finally
                    {
                        InsideResume = false;
                    }
                });

            Source.InsideResume = () => InsideResume;
        }

        internal void Observe(int generation, nint handle = ProducerHandle) =>
            Pump.Observe(new Renderer.SharedTargetHandle(handle, Width, Height, generation));

        // Imports a generation and gets its update loop running.
        internal FakeImage Adopt(int generation)
        {
            Observe(generation);
            FakeImage image = Source.Latest;
            image.CompleteImport();
            return image;
        }
    }

    [Fact]
    public void A_first_generation_is_imported_from_a_duplicate_of_the_producers_handle()
    {
        var rig = new Rig();
        rig.Observe(generation: 4);

        rig.Source.Images.Count.ShouldBe(1);
        rig.Source.ImportedHandles.ShouldBe([ProducerHandle + 0x10000]);
        rig.Pump.LiveGeneration.ShouldBe(4);
    }

    [Fact]
    public void The_duplicate_is_closed_once_the_import_has_completed_and_not_before()
    {
        var rig = new Rig();
        rig.Observe(generation: 1);

        // Closing sooner races the compositor's open on its render thread.
        rig.Closed.ShouldBeEmpty();

        rig.Source.Latest.CompleteImport();
        rig.Closed.ShouldBe([ProducerHandle + 0x10000]);
    }

    [Fact]
    public void The_same_generation_is_never_imported_twice()
    {
        var rig = new Rig();
        rig.Adopt(generation: 7);

        rig.Observe(7);
        rig.Observe(7);

        rig.Source.Images.Count.ShouldBe(1);
    }

    [Fact]
    public void The_consumer_acquires_the_key_the_producer_released()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);

        // Producer takes key 0 and releases 1, so this side takes 1 and hands
        // 0 back. Releasing the acquired key deadlocks both on the next frame.
        image.LastAcquireKey.ShouldBe((uint)Renderer.SharedConsumerKey);
        image.LastReleaseKey.ShouldBe((uint)Renderer.SharedProducerKey);
    }

    [Fact]
    public void The_update_loop_reschedules_itself()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);

        // Adopting fills the queue to HandOverDepth.
        image.Updates.ShouldBe(CompositedFramePump.HandOverDepth);
        image.CompleteUpdate();
        image.Updates.ShouldBe(CompositedFramePump.HandOverDepth + 1);
        image.CompleteUpdate();
        image.Updates.ShouldBe(CompositedFramePump.HandOverDepth + 2);
    }

    // With one hand-over in flight the next often misses the compositor's tick
    // and waits a whole refresh: about 40 hand-overs a second against 60 at two.
    [Fact]
    public void The_queue_is_kept_full_so_the_compositor_never_idles()
    {
        // Asserted as a number: comparing only against the constant would
        // pass at a depth of one.
        CompositedFramePump.HandOverDepth.ShouldBeGreaterThan(1,
            "one hand-over in flight is one hand-over the compositor is waiting for, " +
            "and it idles a whole refresh about half the time");

        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);

        image.UpdatesInFlight.ShouldBe(CompositedFramePump.HandOverDepth);

        for (int lap = 0; lap < 4; lap++)
        {
            image.CompleteUpdate();
            image.UpdatesInFlight.ShouldBe(CompositedFramePump.HandOverDepth,
                "a completed hand-over is replaced inside the same resume");
        }
    }

    [Fact]
    public void A_new_generation_retires_the_old_import_and_acknowledges_it()
    {
        var rig = new Rig();
        FakeImage first = rig.Adopt(generation: 1);

        rig.Adopt(generation: 2);
        first.CompleteAllUpdates();

        first.Disposed.ShouldBeTrue();
        rig.Pump.LiveGeneration.ShouldBe(2);
        rig.Pump.RetiredCount.ShouldBe(0);

        // The retired generation, not the live one: the renderer frees
        // everything at or below the number it is given.
        rig.Acknowledged.ShouldBe([1]);
    }

    [Fact]
    public void A_retired_import_is_not_disposed_while_an_update_is_still_in_flight()
    {
        var rig = new Rig();
        FakeImage first = rig.Adopt(generation: 1);
        first.UpdateInFlight.ShouldBeTrue();

        rig.Observe(generation: 2);

        // The compositor is inside the keyed-mutex bracket on this image.
        // Disposing it now crashes in the driver.
        first.Disposed.ShouldBeFalse();
        rig.Pump.RetiredCount.ShouldBe(1);
        rig.Acknowledged.ShouldBeEmpty();

        // Only one of the outstanding hand-overs: settling on the first would
        // free the image under the second.
        first.CompleteUpdate();
        first.Disposed.ShouldBeFalse();

        first.CompleteAllUpdates();

        first.Disposed.ShouldBeTrue();
        rig.Pump.RetiredCount.ShouldBe(0);
        rig.Acknowledged.ShouldBe([1]);
    }

    [Fact]
    public void The_replacement_keeps_pumping_after_the_old_loop_unwinds()
    {
        var rig = new Rig();
        FakeImage first = rig.Adopt(generation: 1);

        // The new import finds the old loop still running and stands down.
        // The unwinding loop has to start it.
        FakeImage second = rig.Adopt(generation: 2);
        second.Updates.ShouldBe(0);

        first.CompleteAllUpdates();

        second.Updates.ShouldBe(CompositedFramePump.HandOverDepth);
        second.CompleteUpdate();
        second.Updates.ShouldBe(CompositedFramePump.HandOverDepth + 1);
    }

    [Fact]
    public void A_generation_the_shell_never_saw_is_covered_by_the_next_acknowledgement()
    {
        var rig = new Rig();
        FakeImage first = rig.Adopt(generation: 1);

        // Two resizes in one pass: generation 2 is never observed. The renderer
        // releases at-or-below, so the next acknowledgement frees it.
        rig.Adopt(generation: 3);
        first.CompleteAllUpdates();

        rig.Acknowledged.ShouldBe([1]);
        rig.Source.Images.Count.ShouldBe(2);
        rig.Pump.LiveGeneration.ShouldBe(3);
    }

    [Fact]
    public void An_import_the_compositor_refuses_is_released_rather_than_left_live()
    {
        var rig = new Rig();
        rig.Observe(generation: 5);

        rig.Source.Latest.FailImport();

        rig.Pump.LiveGeneration.ShouldBe(0);
        rig.Source.Latest.Disposed.ShouldBeTrue();
        rig.Closed.ShouldBe([ProducerHandle + 0x10000]);
        rig.Acknowledged.ShouldBe([5]);
    }

    [Fact]
    public void A_handle_that_cannot_be_duplicated_is_skipped_rather_than_imported()
    {
        var source = new FakeSource();
        var pump = new CompositedFramePump(
            source, _ => { }, NullLogger.Instance, _ => 0, _ => { });

        // The producer retired its handle after publishing: a resize outran the shell.
        pump.Observe(new Renderer.SharedTargetHandle(ProducerHandle, Width, Height, 2));

        source.Images.ShouldBeEmpty();
        pump.LiveGeneration.ShouldBe(0);
    }

    [Fact]
    public void Nothing_is_imported_for_a_target_that_does_not_exist_yet()
    {
        var rig = new Rig();

        rig.Pump.Observe(new Renderer.SharedTargetHandle(0, Width, Height, 1));
        rig.Pump.Observe(new Renderer.SharedTargetHandle(ProducerHandle, 0, Height, 1));

        rig.Source.Images.ShouldBeEmpty();
    }

    [Fact]
    public void A_hidden_viewport_stops_pumping_and_starts_again_when_it_comes_back()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);
        image.CompleteUpdate();

        int taken = image.Updates;
        rig.Pump.SetVisible(false);

        // All of them: the loop ends when the last one lands.
        image.CompleteAllUpdates();
        image.Updates.ShouldBe(taken);
        rig.Pump.IsPumping.ShouldBeFalse();

        rig.Pump.SetVisible(true);
        image.Updates.ShouldBe(taken + CompositedFramePump.HandOverDepth);
    }

    [Fact]
    public void Stopping_retires_the_live_import_and_refuses_anything_further()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 3);

        rig.Pump.Stop();
        image.CompleteAllUpdates();

        image.Disposed.ShouldBeTrue();
        rig.Acknowledged.ShouldBe([3]);
        rig.Pump.LiveGeneration.ShouldBe(0);

        rig.Observe(generation: 4);
        rig.Source.Images.Count.ShouldBe(1);
    }

    [Fact]
    public void A_stop_during_a_hand_over_still_waits_for_it()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);
        image.UpdateInFlight.ShouldBeTrue();

        rig.Pump.Stop();

        // The producer is still there to release the key this hand-over waits on.
        image.Disposed.ShouldBeFalse();

        image.CompleteUpdate();
        image.Disposed.ShouldBeFalse();

        image.CompleteAllUpdates();
        image.Disposed.ShouldBeTrue();
        rig.Acknowledged.ShouldBe([1]);
    }

    // A dock drag detaches and re-attaches the pane. Disposing the surface at
    // detach would be a disposal under a live keyed-mutex bracket.
    [Fact]
    public void The_source_is_released_only_after_the_last_hand_over_has_finished()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 2);
        image.UpdateInFlight.ShouldBeTrue();

        rig.Pump.Stop();
        rig.Source.Disposed.ShouldBeFalse();
        rig.Pump.SourceReleased.ShouldBeFalse();

        image.CompleteAllUpdates();

        rig.Source.Disposed.ShouldBeTrue();
        rig.Pump.SourceReleased.ShouldBeTrue();
    }

    [Fact]
    public void A_pump_that_never_imported_anything_releases_its_source_at_once()
    {
        var rig = new Rig();

        rig.Pump.Stop();

        rig.Source.Disposed.ShouldBeTrue();
    }

    [Fact]
    public void A_superseded_generation_does_not_release_the_source_under_the_live_one()
    {
        // The live import still snapshots into the source's surface.
        var rig = new Rig();
        FakeImage first = rig.Adopt(generation: 1);

        rig.Observe(generation: 2);
        first.CompleteAllUpdates();

        first.Disposed.ShouldBeTrue();
        rig.Source.Disposed.ShouldBeFalse();
        rig.Pump.LiveGeneration.ShouldBe(2);
    }

    [Fact]
    public void The_source_waits_for_a_retired_import_as_well_as_the_live_one()
    {
        var rig = new Rig();
        FakeImage first = rig.Adopt(generation: 1);

        // A detach mid-resize: the first import is still inside its hand-over.
        rig.Observe(generation: 2);
        FakeImage second = rig.Source.Latest;
        second.CompleteImport();
        first.UpdateInFlight.ShouldBeTrue();

        // The live import never got a loop of its own, so it settles at once.
        rig.Pump.Stop();
        second.Disposed.ShouldBeTrue();
        rig.Source.Disposed.ShouldBeFalse();

        first.CompleteAllUpdates();

        first.Disposed.ShouldBeTrue();
        rig.Source.Disposed.ShouldBeTrue();
        rig.Acknowledged.ShouldBe([2, 1]);
    }

    // The compositor completes a hand-over on its own render thread. The next
    // UpdateAsync, the retirement bookkeeping and the fault report all need
    // the loop posted back to the UI thread first.

    [Fact]
    public void The_next_hand_over_waits_for_the_loop_to_be_resumed_on_the_ui_thread()
    {
        var rig = new Rig { HoldResumes = true };
        FakeImage image = rig.Adopt(generation: 1);
        image.Updates.ShouldBe(CompositedFramePump.HandOverDepth);

        image.CompleteUpdate();
        image.Updates.ShouldBe(CompositedFramePump.HandOverDepth);
        rig.PendingResumes.ShouldBe(1);

        rig.ReleaseResumes();
        image.Updates.ShouldBe(CompositedFramePump.HandOverDepth + 1);
    }

    [Fact]
    public void A_retired_import_is_not_disposed_until_the_loop_is_back_on_the_ui_thread()
    {
        var rig = new Rig { HoldResumes = true };
        FakeImage first = rig.Adopt(generation: 1);
        rig.Observe(generation: 2);

        first.CompleteAllUpdates();
        first.Disposed.ShouldBeFalse();
        rig.Acknowledged.ShouldBeEmpty();

        rig.ReleaseResumes();
        first.Disposed.ShouldBeTrue();
        rig.Acknowledged.ShouldBe([1]);
    }

    [Fact]
    public void A_hand_over_that_throws_reports_its_fault_from_the_ui_thread()
    {
        var rig = new Rig { HoldResumes = true };
        FakeImage image = rig.Adopt(generation: 1);

        image.FailUpdate();
        rig.Faults.ShouldBe(0);
        rig.Pump.IsStalled.ShouldBeFalse();

        rig.ReleaseResumes();
        rig.Faults.ShouldBe(1);
        rig.Pump.IsStalled.ShouldBeTrue();
    }

    [Fact]
    public void Every_hand_over_costs_exactly_one_resume()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);

        rig.Resumes.ShouldBe(0);
        image.CompleteUpdate();
        rig.Resumes.ShouldBe(1);
        image.CompleteUpdate();
        rig.Resumes.ShouldBe(2);
    }

    // The ordering tests above check that a resume happens. This checks where
    // the work after it runs: awaiting a task the post completes can continue
    // on the compositor's thread if the post ran before the awaiter attached.
    [Fact]
    public void Every_hand_over_after_the_first_is_issued_from_inside_the_UI_thread_post()
    {
        var rig = new Rig();
        FakeImage image = rig.Adopt(generation: 1);

        for (int i = 0; i < 4; i++)
            image.CompleteUpdate();

        image.Updates.ShouldBeGreaterThan(1, "the loop must have re-issued");

        // The first HandOverDepth come from StartLoop, not from a resume.
        image.UpdateSites.Count.ShouldBe(image.Updates);
        image.UpdateSites.Skip(CompositedFramePump.HandOverDepth).ShouldAllBe(inside => inside,
            "a hand-over issued after the post returned is issued off the UI thread, " +
            "where UpdateAsync verifies access and throws");
    }
}
