using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The generation bookkeeping behind a shared target that is rebuilt rather than
/// resized.
/// </summary>
public sealed class SharedTargetRetirementTests
{
    private static SharedTargetRetirement New() => new(NullLogger.Instance);

    [Fact]
    public void Generations_start_at_one_and_never_repeat()
    {
        // Zero means "no target yet".
        var retirement = New();

        retirement.CurrentGeneration.ShouldBe(0);
        retirement.Next().ShouldBe(1);
        retirement.Next().ShouldBe(2);
        retirement.Next().ShouldBe(3);
        retirement.CurrentGeneration.ShouldBe(3);
    }

    [Fact]
    public void A_retired_generation_is_not_released_until_the_consumer_says_so()
    {
        var released = new List<int>();
        var retirement = New();

        int first = retirement.Next();
        retirement.Retire(first, () => released.Add(first));

        released.ShouldBeEmpty();
        retirement.PendingCount.ShouldBe(1);

        retirement.ConsumerReleased(first).ShouldBe(1);

        released.ShouldBe([first]);
        retirement.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void An_acknowledgement_releases_every_older_generation_too()
    {
        // Two resizes inside one consumer frame leave a generation it never
        // imported, so it will never acknowledge that one by number.
        var released = new List<int>();
        var retirement = New();

        for (int i = 0; i < 3; i++)
        {
            int generation = retirement.Next();
            retirement.Retire(generation, () => released.Add(generation));
        }

        retirement.ConsumerReleased(3).ShouldBe(3);
        released.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Generations_are_released_oldest_first()
    {
        var released = new List<int>();
        var retirement = New();

        for (int i = 0; i < 4; i++)
        {
            int generation = retirement.Next();
            retirement.Retire(generation, () => released.Add(generation));
        }

        retirement.ConsumerReleased(2);
        released.ShouldBe([1, 2]);

        retirement.ConsumerReleased(4);
        released.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void An_acknowledgement_for_a_generation_still_live_releases_nothing()
    {
        var released = new List<int>();
        var retirement = New();

        int retired = retirement.Next();
        retirement.Retire(retired, () => released.Add(retired));
        int live = retirement.Next();

        retirement.ConsumerReleased(retired).ShouldBe(1);
        retirement.ConsumerReleased(live).ShouldBe(0);
        released.ShouldBe([retired]);
    }

    [Fact]
    public void Past_the_cap_the_oldest_is_released_without_its_acknowledgement()
    {
        // A consumer that never acknowledges would otherwise pin one surface
        // per resize step.
        var released = new List<int>();
        var retirement = New();

        for (int i = 0; i < SharedTargetRetirement.Cap + 3; i++)
        {
            int generation = retirement.Next();
            retirement.Retire(generation, () => released.Add(generation));
        }

        retirement.PendingCount.ShouldBe(SharedTargetRetirement.Cap);
        retirement.ForcedReleaseCount.ShouldBe(3);
        released.ShouldBe([1, 2, 3], "the cap releases the OLDEST, which is the one the consumer is least likely to still hold");
    }

    [Fact]
    public void The_forced_release_count_is_readable_and_not_only_logged()
    {
        var retirement = New();

        retirement.ForcedReleaseCount.ShouldBe(0);

        for (int i = 0; i < SharedTargetRetirement.Cap + 1; i++)
            retirement.Retire(retirement.Next(), () => { });

        retirement.ForcedReleaseCount.ShouldBe(1);
    }

    [Fact]
    public void Shutdown_releases_everything_regardless_of_acknowledgement()
    {
        var released = new List<int>();
        var retirement = New();

        for (int i = 0; i < 3; i++)
        {
            int generation = retirement.Next();
            retirement.Retire(generation, () => released.Add(generation));
        }

        retirement.ReleaseAll();

        released.ShouldBe([1, 2, 3]);
        retirement.PendingCount.ShouldBe(0);
        Should.NotThrow(retirement.ReleaseAll);
    }

    [Fact]
    public void A_release_that_retires_again_does_not_corrupt_the_list()
    {
        var retirement = New();
        var released = new List<int>();

        int first = retirement.Next();
        retirement.Retire(first, () =>
        {
            released.Add(first);
            retirement.Retire(retirement.Next(), () => released.Add(99));
        });

        retirement.ReleaseAll();

        released.ShouldBe([first]);
        retirement.PendingCount.ShouldBe(1, "what the callback added stays pending rather than being swallowed");
    }

    [Fact]
    public void A_null_release_is_refused_where_it_is_written()
    {
        var retirement = New();

        Should.Throw<ArgumentNullException>(() => retirement.Retire(retirement.Next(), null!));
    }

    // A consumer may still be waiting on a retired generation's key, on its own
    // render thread with a deadline of about 24 days. An unanswered turn there
    // freezes the UI, so retired generations keep being offered their key.

    [Fact]
    public void Every_retired_generation_is_offered_its_key_each_time_turns_are_offered()
    {
        var offered = new List<int>();
        var retirement = New();

        int first = retirement.Next();
        retirement.Retire(first, () => { }, () => offered.Add(first));
        int second = retirement.Next();
        retirement.Retire(second, () => { }, () => offered.Add(second));

        retirement.OfferTurns();
        offered.ShouldBe([first, second]);

        // Every frame: the consumer's turn may arrive several frames later.
        retirement.OfferTurns();
        offered.ShouldBe([first, second, first, second]);
    }

    [Fact]
    public void An_acknowledged_generation_is_no_longer_offered_anything()
    {
        // An offer past the acknowledgement would touch a destroyed texture's mutex.
        var offered = new List<int>();
        var retirement = New();

        int generation = retirement.Next();
        retirement.Retire(generation, () => { }, () => offered.Add(generation));

        retirement.ConsumerReleased(generation);
        retirement.OfferTurns();

        offered.ShouldBeEmpty();
    }

    [Fact]
    public void A_generation_retired_without_an_offer_is_simply_not_offered_one()
    {
        var retirement = New();
        int generation = retirement.Next();

        retirement.Retire(generation, () => { });

        Should.NotThrow(retirement.OfferTurns);
    }
}
