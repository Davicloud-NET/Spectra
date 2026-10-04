using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

// Reclaim order: free, then the oldest finished source, then a playing one.
public sealed class AudioSourcePoolTests
{
    [Fact]
    public void A_pool_sizes_itself_to_what_the_driver_actually_granted()
    {
        // A driver with a hard limit refuses partway through. Handing out
        // handle 0 afterwards would be accepted by AL and ignored.
        var backend = new FakeAudioBackend(maxSources: 5);
        var pool = new AudioSourcePool(backend, 32);

        pool.Capacity.ShouldBe(5);
        backend.LiveSourceCount.ShouldBe(5);
    }

    [Fact]
    public void An_exhausted_pool_reclaims_the_oldest_finished_source_and_not_a_playing_one()
    {
        var backend = new FakeAudioBackend(maxSources: 3);
        var pool = new AudioSourcePool(backend, 3);

        pool.TryAcquire(streaming: false, out uint first).ShouldBeTrue();
        pool.TryAcquire(streaming: false, out uint second).ShouldBeTrue();
        pool.TryAcquire(streaming: false, out uint third).ShouldBeTrue();
        backend.Play(first);
        backend.Play(second);
        backend.Play(third);

        // Finished in reverse of acquire order: the pool picks by acquire order.
        backend.Finish(third);
        backend.Finish(second);

        pool.TryAcquire(streaming: false, out uint reclaimed).ShouldBeTrue();
        reclaimed.ShouldBe(second);
        pool.StolenCount.ShouldBe(0);

        // A voice plays its source at once, so the reclaimed one is busy again.
        backend.Play(reclaimed);

        pool.TryAcquire(streaming: false, out uint next).ShouldBeTrue();
        next.ShouldBe(third);
        pool.StolenCount.ShouldBe(0);
        backend.Play(next);

        // Nothing finished is left, so a playing source is stolen and counted.
        pool.TryAcquire(streaming: false, out uint stolen).ShouldBeTrue();
        stolen.ShouldBe(first);
        pool.StolenCount.ShouldBe(1);
    }

    [Fact]
    public void A_streaming_source_is_never_reclaimed_by_the_state_scan()
    {
        // A starved streaming source reports Stopped, the same as a finished one.
        var backend = new FakeAudioBackend(maxSources: 2);
        var pool = new AudioSourcePool(backend, 2);

        pool.TryAcquire(streaming: true, out uint music).ShouldBeTrue();
        pool.TryAcquire(streaming: false, out uint shot).ShouldBeTrue();
        backend.Play(music);
        backend.Play(shot);

        backend.Starve(music);
        backend.Finish(shot);

        backend.StateOf(music).ShouldBe(AudioSourceState.Stopped);
        pool.TryAcquire(streaming: false, out uint reclaimed).ShouldBeTrue();
        reclaimed.ShouldBe(shot);
    }

    [Fact]
    public void A_pool_carrying_only_streams_drops_the_new_sound_rather_than_cutting_music()
    {
        var backend = new FakeAudioBackend(maxSources: 2);
        var pool = new AudioSourcePool(backend, 2);

        pool.TryAcquire(streaming: true, out _).ShouldBeTrue();
        pool.TryAcquire(streaming: true, out _).ShouldBeTrue();

        pool.TryAcquire(streaming: false, out uint source).ShouldBeFalse();
        source.ShouldBe(0u);
        pool.StarvedCount.ShouldBe(1);
        pool.StolenCount.ShouldBe(0);
    }

    [Fact]
    public void A_released_source_is_detached_before_it_is_handed_out_again()
    {
        // AL refuses to queue onto a source that still holds a static buffer.
        var backend = new FakeAudioBackend(maxSources: 1);
        var pool = new AudioSourcePool(backend, 1);

        pool.TryAcquire(streaming: false, out uint source).ShouldBeTrue();
        backend.SetSourceBuffer(source, 7);
        pool.Release(source);

        pool.TryAcquire(streaming: true, out uint reused).ShouldBeTrue();
        reused.ShouldBe(source);
        backend.QueueBuffer(reused, 9);
        backend.QueueDepth(reused).ShouldBe(1);
    }

    [Fact]
    public void Releasing_a_free_or_foreign_handle_is_a_no_op()
    {
        var backend = new FakeAudioBackend(maxSources: 2);
        var pool = new AudioSourcePool(backend, 2);

        pool.TryAcquire(streaming: false, out uint source).ShouldBeTrue();
        pool.Release(source);
        pool.InUse.ShouldBe(0);

        // Not covered: a stale release of a reused handle. The pool cannot
        // detect that; AudioManager drops the voice when it releases the source.
        pool.Release(source);
        pool.Release(9999);
        pool.InUse.ShouldBe(0);
    }
}
