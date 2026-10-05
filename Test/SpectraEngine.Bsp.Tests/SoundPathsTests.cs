using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

public sealed class SoundPathsTests
{
    private static readonly SoundPath Through = new(new Vector3(1, 2, 3), 0.5f, 0.25f);
    private static readonly SoundPath Around = new(new Vector3(4, 5, 6), 0.75f, 1f);

    [Fact]
    public void An_emitter_with_no_path_has_none_to_read()
    {
        SoundPaths.None.Count.ShouldBe(0);
        default(SoundPaths).Count.ShouldBe(0);

        Should.Throw<ArgumentOutOfRangeException>(() => SoundPaths.None[0]);
    }

    [Fact]
    public void One_path_is_read_back_and_a_second_is_not_there()
    {
        var paths = new SoundPaths(Through);

        paths.Count.ShouldBe(1);
        paths[0].ShouldBe(Through);
        Should.Throw<ArgumentOutOfRangeException>(() => paths[1]);
    }

    [Fact]
    public void Two_paths_are_read_back_in_the_order_given()
    {
        var paths = new SoundPaths(Through, Around);

        paths.Count.ShouldBe(SoundPaths.Capacity);
        paths[0].ShouldBe(Through);
        paths[1].ShouldBe(Around);
        Should.Throw<ArgumentOutOfRangeException>(() => paths[2]);
        Should.Throw<ArgumentOutOfRangeException>(() => paths[-1]);
    }
}
