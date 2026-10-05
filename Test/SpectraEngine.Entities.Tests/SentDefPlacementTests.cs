using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities.Tests;

public sealed class SentDefPlacementTests
{
    private static int PlacementByte => SentDef.HeaderSize + SentDef.TypeRecordPlacementOffset;

    // Three bare classes, a Brush, a Point and an Abstract, laid out by hand
    // from the format. A build that does not know Volume writes the same.
    private static byte[] OlderImage =>
    [
        (byte)'S', (byte)'E', (byte)'N', (byte)'T', 1, 0, 20, 0, 3, 0, 0, 0, 92, 0, 0, 0, 36, 0, 0, 0,
        24, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0,
        24, 0, 0, 0, 13, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        24, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0,
        0, 0,
        9, 0, .. "func_wall"u8,
        9, 0, .. "info_spot"u8,
        10, 0, .. "logic_gate"u8,
    ];

    private static EntitySchema Trigger(EntityPlacement placement) => new("trigger_once", placement: placement);

    [Fact]
    public void An_image_with_only_the_three_older_placements_still_reads()
    {
        EntitySchema[] read = SentDef.Read(OlderImage);

        read.Select(schema => schema.ClassName).ShouldBe(["func_wall", "info_spot", "logic_gate"]);
        read.Select(schema => schema.Placement).ShouldBe(
            [EntityPlacement.Brush, EntityPlacement.Point, EntityPlacement.Abstract]);
    }

    [Fact]
    public void The_three_older_placements_keep_their_bytes()
    {
        byte[] image = SentDef.Write(
        [
            new EntitySchema("func_wall", placement: EntityPlacement.Brush),
            new EntitySchema("info_spot"),
            new EntitySchema("logic_gate", placement: EntityPlacement.Abstract),
        ]);

        image.ShouldBe(OlderImage);
    }

    [Fact]
    public void A_volume_class_survives_the_round_trip()
    {
        EntitySchema copy = SentDef.Read(SentDef.Write([Trigger(EntityPlacement.Volume)]))[0];

        copy.Placement.ShouldBe(EntityPlacement.Volume);
    }

    [Fact]
    public void A_volume_class_differs_from_a_brush_class_in_the_placement_byte_alone()
    {
        byte[] brush = SentDef.Write([Trigger(EntityPlacement.Brush)]);
        byte[] volume = SentDef.Write([Trigger(EntityPlacement.Volume)]);

        volume.Length.ShouldBe(brush.Length);

        var differences = new List<int>();
        for (int i = 0; i < brush.Length; i++)
        {
            if (brush[i] != volume[i])
                differences.Add(i);
        }

        differences.ShouldBe([PlacementByte]);
        volume[PlacementByte].ShouldBe((byte)3);
    }

    [Fact]
    public void A_placement_byte_past_the_last_one_named_is_refused_on_read()
    {
        // No fallback to Point: the editor would offer a brush class as a point.
        byte[] image = SentDef.Write([Trigger(EntityPlacement.Volume)]);
        image[PlacementByte] = 4;

        Should.Throw<SentDefFormatException>(() => SentDef.Read(image))
            .Message.ShouldContain("EntityPlacement");
    }
}
