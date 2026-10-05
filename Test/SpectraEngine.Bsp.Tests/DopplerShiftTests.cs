using SpectraEngine.Core.Audio.Propagation;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The pitch a path is heard at, from how fast it gets longer or shorter. In
/// numbers, with no scene and no audio device.
/// </summary>
public sealed class DopplerShiftTests
{
    // How close a steady factor comes to the speed of sound over the speed
    // of sound plus the rate.
    private const double Tolerance = 0.0005;

    // The most a steady path's factor wanders, in cents, at 60, 144 and 240
    // frames a second. Measured: 0.004, which is the rounding of the lengths.
    private const double Flutter = 0.05;

    // The same where the frame rate is near the tick rate and not on it, so
    // a frame now and then holds no tick or two. Measured: up to 1.3, as a
    // slow drift and not a buzz.
    private const double FlutterNearTheTickRate = 2.0;

    // Seconds for a path that stopped at 20 units a second to be heard
    // within a cent of its own pitch, and to be at it. Measured: 0.20 and 0.29.
    private const double SettledSeconds = 0.25;
    private const double RestSeconds = 0.35;

    private const double Closing = 343.0 / (343.0 - 34.3);
    private const double Parting = 343.0 / (343.0 + 34.3);

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_that_closes_in_at_a_tenth_of_the_speed_of_sound_is_heard_at_1_111(int framesPerSecond)
    {
        var bench = new DopplerBench(framesPerSecond, (ticked, _) => 500 - (34.3 * ticked));

        bench.Run(1);

        Closing.ShouldBe(1.111, 0.0005);
        ((double)bench.Factor).ShouldBe(Closing, Tolerance);
        ((double)bench.Rate).ShouldBe(-34.3, 0.05);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_that_parts_at_a_tenth_of_the_speed_of_sound_is_heard_at_0_909(int framesPerSecond)
    {
        var bench = new DopplerBench(framesPerSecond, (ticked, _) => 20 + (34.3 * ticked));

        bench.Run(1);

        Parting.ShouldBe(0.909, 0.0005);
        ((double)bench.Factor).ShouldBe(Parting, Tolerance);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_listener_that_closes_in_is_heard_the_same_as_a_sound_that_does(int framesPerSecond)
    {
        // The sound moves on the ticks and the listener on the frames.
        var sound = new DopplerBench(framesPerSecond, (ticked, _) => 500 - (34.3 * ticked));
        var listener = new DopplerBench(framesPerSecond, (_, now) => 500 - (34.3 * now));

        sound.Run(1);
        listener.Run(1);

        ((double)listener.Factor).ShouldBe(Closing, Tolerance);
        ((double)listener.Factor).ShouldBe(sound.Factor, 0.0001);
    }

    [Fact]
    public void Both_ends_moving_add_up()
    {
        var bench = new DopplerBench(144, (ticked, now) => 500 - (20 * ticked) - (14.3 * now));

        bench.Run(1);

        ((double)bench.Factor).ShouldBe(Closing, Tolerance);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_path_that_keeps_its_length_is_heard_at_exactly_its_own_pitch(int framesPerSecond)
    {
        var bench = new DopplerBench(framesPerSecond, (_, _) => 12.5);

        for (int frame = 0; frame < 300; frame++)
            bench.Step().ShouldBe(1f);
    }

    [Fact]
    public void The_first_length_shifts_nothing()
    {
        var clock = new DopplerClock();
        var shift = new DopplerShift();
        shift.Factor.ShouldBe(1f);

        clock.Advance(1f / 60f, 1f / 60f);
        shift.Step(40f, in clock);

        shift.Factor.ShouldBe(1f);
        shift.Rate.ShouldBe(0f);
    }

    [Fact]
    public void A_jump_shifts_nothing_on_its_frame_and_leaves_no_chirp()
    {
        // Still at 40 units for a second, then 15 units away from one frame to the next.
        var bench = new DopplerBench(144, (ticked, _) => ticked < 1 ? 40 : 15);
        bench.Run(0.9);

        for (int frame = 0; frame < 144; frame++)
            bench.Step().ShouldBe(1f);
    }

    [Fact]
    public void A_sound_that_jumps_while_it_moves_starts_over_and_never_passes_its_steady_factor()
    {
        var bench = new DopplerBench(144, (ticked, _) => (ticked < 1 ? 500 : 300) - (20 * ticked));
        bench.Run(0.9);
        float steady = bench.Factor;

        float lowest = float.MaxValue;
        float highest = float.MinValue;
        while (bench.FrameSeconds < 2)
        {
            float factor = bench.Step();
            lowest = MathF.Min(lowest, factor);
            highest = MathF.Max(highest, factor);
        }

        lowest.ShouldBe(1f);
        highest.ShouldBeLessThanOrEqualTo(steady + 0.0001f);
        ((double)bench.Factor).ShouldBe(steady, Tolerance);
    }

    [Fact]
    public void A_jump_the_caller_knows_of_shifts_nothing_even_when_the_length_hardly_moved()
    {
        var bench = new DopplerBench(60, (ticked, _) => 500 - (20 * ticked));
        bench.Run(1);
        bench.Factor.ShouldBeGreaterThan(1.05f);

        bench.Step(jumped: true).ShouldBe(1f);
    }

    [Fact]
    public void A_shift_that_missed_a_frame_starts_over()
    {
        var clock = new DopplerClock();
        var shift = new DopplerShift();
        for (int frame = 0; frame < 60; frame++)
        {
            clock.Advance(1f / 60f, 1f / 60f);
            shift.Step(100f - (frame * 0.5f), in clock);
        }

        shift.Factor.ShouldBeGreaterThan(1.05f);

        // Not stepped on this frame: silent, or switched off.
        clock.Advance(1f / 60f, 1f / 60f);
        clock.Advance(1f / 60f, 1f / 60f);
        shift.Step(69f, in clock);

        shift.Factor.ShouldBe(1f);
    }

    [Fact]
    public void A_clock_that_was_reset_starts_every_shift_over()
    {
        var clock = new DopplerClock();
        var shift = new DopplerShift();
        for (int frame = 0; frame < 60; frame++)
        {
            clock.Advance(1f / 60f, 1f / 60f);
            shift.Step(100f - (frame * 0.5f), in clock);
        }

        clock.Reset();
        clock.Advance(1f / 60f, 1f / 60f);
        shift.Step(70f, in clock);

        shift.Factor.ShouldBe(1f);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_length_that_is_not_a_number_shifts_nothing(float broken)
    {
        var clock = new DopplerClock();
        var shift = new DopplerShift();
        for (int frame = 0; frame < 30; frame++)
        {
            clock.Advance(1f / 60f, 1f / 60f);
            shift.Step(100f - (frame * 0.5f), in clock);
        }

        clock.Advance(1f / 60f, 1f / 60f);
        shift.Step(broken, in clock);
        shift.Factor.ShouldBe(1f);

        clock.Advance(1f / 60f, 1f / 60f);
        shift.Step(85f, in clock);
        shift.Factor.ShouldBe(1f);
    }

    [Fact]
    public void A_frame_that_took_no_time_changes_nothing()
    {
        var clock = new DopplerClock();
        var shift = new DopplerShift();
        for (int frame = 0; frame < 60; frame++)
        {
            clock.Advance(1f / 60f, 1f / 60f);
            shift.Step(100f - (frame * 0.5f), in clock);
        }

        float before = shift.Factor;

        clock.Advance(0f, 0f);
        shift.Step(70.5f, in clock);

        shift.Factor.ShouldBe(before);
    }

    [Fact]
    public void Strength_0_shifts_nothing_and_2_doubles_the_shift_in_cents()
    {
        var real = new DopplerBench(144, (ticked, _) => 500 - (34.3 * ticked));
        var none = new DopplerBench(144, (ticked, _) => 500 - (34.3 * ticked)) { Strength = 0f };
        var doubled = new DopplerBench(144, (ticked, _) => 500 - (34.3 * ticked)) { Strength = 2f };

        real.Run(1);
        none.Run(1);
        doubled.Run(1);

        none.Factor.ShouldBe(1f);
        DopplerBench.Cents(doubled.Factor).ShouldBe(2 * DopplerBench.Cents(real.Factor), 0.01);
        DopplerBench.Cents(real.Factor).ShouldBe(182.4, 0.5);
    }

    [Theory]
    [InlineData(-34.3f, 1f, 1.1111f)]
    [InlineData(34.3f, 1f, 0.9091f)]
    [InlineData(0f, 1f, 1f)]
    [InlineData(0f, 4f, 1f)]
    [InlineData(-171.5f, 1f, 2f)]
    [InlineData(-300f, 1f, 2f)]
    [InlineData(-343f, 1f, 2f)]
    [InlineData(-5000f, 1f, 2f)]
    [InlineData(343f, 1f, 0.5f)]
    [InlineData(5000f, 1f, 0.5f)]
    [InlineData(float.PositiveInfinity, 1f, 0.5f)]
    [InlineData(float.NegativeInfinity, 1f, 2f)]
    [InlineData(-100f, 4f, 2f)]
    [InlineData(100f, 4f, 0.5f)]
    [InlineData(float.NaN, 1f, 1f)]
    [InlineData(-34.3f, float.NaN, 1f)]
    public void The_factor_is_the_speed_of_sound_over_itself_plus_the_rate_and_never_leaves_a_half_to_2(
        float rate, float strength, float expected)
    {
        DopplerShift.FactorFor(rate, strength).ShouldBe(expected, 0.0001f);
    }

    [Fact]
    public void No_motion_however_wild_takes_the_factor_out_of_a_half_to_2()
    {
        var random = new Random(7);
        var clock = new DopplerClock();
        var shift = new DopplerShift();
        float length = 50f;

        for (int frame = 0; frame < 20_000; frame++)
        {
            float seconds = 0.001f + ((float)random.NextDouble() * 0.05f);
            int ticks = random.Next(0, 4);
            clock.Advance(seconds, ticks / 60f);

            length = MathF.Max(0f, length + (((float)random.NextDouble() - 0.5f) * 6f));
            shift.Step(length, in clock, strength: 4f);

            shift.Factor.ShouldBeInRange(DopplerShift.MinFactor, DopplerShift.MaxFactor);
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_that_moves_in_ticks_at_20_units_a_second_does_not_flutter(int framesPerSecond)
    {
        var bench = new DopplerBench(framesPerSecond, (ticked, _) => 500 - (20 * ticked));
        bench.Run(1);

        (double lowest, double highest) = bench.CentsOver(4);

        (highest - lowest).ShouldBeLessThan(Flutter);
        lowest.ShouldBe(DopplerBench.Cents(343.0 / 323.0), Flutter);
    }

    [Theory]
    [InlineData(59, 0)]
    [InlineData(61, 0)]
    [InlineData(60, 0.01)]
    [InlineData(60, 0.05)]
    [InlineData(30, 0.05)]
    [InlineData(144, 0.3)]
    public void Frames_that_do_not_line_up_with_the_ticks_wander_by_little(int framesPerSecond, double unevenness)
    {
        var sound = new DopplerBench(framesPerSecond, (ticked, _) => 500 - (20 * ticked), unevenness);
        var listener = new DopplerBench(framesPerSecond, (_, now) => 500 - (20 * now), unevenness);
        sound.Run(1);
        listener.Run(1);

        (double soundLowest, double soundHighest) = sound.CentsOver(6);
        (double listenerLowest, double listenerHighest) = listener.CentsOver(6);

        (soundHighest - soundLowest).ShouldBeLessThan(FlutterNearTheTickRate);
        (listenerHighest - listenerLowest).ShouldBeLessThan(FlutterNearTheTickRate);
    }

    [Theory]
    [InlineData(144, 0.35)]
    [InlineData(240, 0.05)]
    public void A_sound_passing_by_is_heard_as_it_is_at_one_frame_a_tick(int framesPerSecond, double most)
    {
        // Past the listener at 20 units a second, 2 units away at its closest.
        static double Pass(double ticked, double now) => Math.Sqrt(4 + Math.Pow(20 * (ticked - 2), 2));

        var locked = new DopplerBench(60, Pass);
        var atTick = new Dictionary<long, double>();
        while (locked.FrameSeconds < 4)
        {
            double cents = DopplerBench.Cents(locked.Step());
            atTick[locked.Ticks] = cents;
        }

        // Not the first half second: the two start a tick apart, and the
        // shift is still coming in.
        var bench = new DopplerBench(framesPerSecond, Pass);
        double worst = 0;
        while (bench.FrameSeconds < 4)
        {
            double cents = DopplerBench.Cents(bench.Step());
            if (bench.FrameSeconds > 0.5 && atTick.TryGetValue(bench.Ticks, out double reference))
                worst = Math.Max(worst, Math.Abs(cents - reference));
        }

        // The whole swing is there to be compared: from closing to parting.
        atTick.Values.Max().ShouldBeGreaterThan(95);
        atTick.Values.Min().ShouldBeLessThan(-95);
        worst.ShouldBeLessThan(most);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_that_stops_is_back_at_its_own_pitch_in_a_third_of_a_second(int framesPerSecond)
    {
        var bench = new DopplerBench(framesPerSecond, (ticked, _) => 500 - (20 * Math.Min(ticked, 1)));
        bench.Run(1);
        DopplerBench.Cents(bench.Factor).ShouldBeGreaterThan(100);

        bench.Run(SettledSeconds);
        Math.Abs(DopplerBench.Cents(bench.Factor)).ShouldBeLessThan(1);

        bench.Run(RestSeconds - SettledSeconds);
        bench.Factor.ShouldBe(1f);
        bench.Rate.ShouldBe(0f);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_that_sets_off_is_heard_nine_tenths_shifted_a_tenth_of_a_second_on(int framesPerSecond)
    {
        var bench = new DopplerBench(framesPerSecond, (ticked, _) => 500 - (20 * Math.Max(ticked - 1, 0)));
        bench.Run(1);
        bench.Factor.ShouldBe(1f);

        bench.Run(0.1);

        double full = DopplerBench.Cents(343.0 / 323.0);
        DopplerBench.Cents(bench.Factor).ShouldBeInRange(0.9 * full, full);
    }
}
