using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The Receives and Now sections of the property panel: the wires that arrive
/// at the selected entity, and its state while the level runs.
/// </summary>
public sealed class EntityArrivalsPanelTests
{
    private static readonly Guid NodeId = Guid.Parse("3f2a1c88-4b6d-4a19-9d0e-77c1f0a2b3e4");
    private static readonly Guid Button = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Zone = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");

    private readonly PropertyPanelModel _panel = new(_ => { }, _ => { }, _ => { });

    private static EntityPanelInfo Info(
        IReadOnlyList<EntityIncomingInfo>? incoming = null,
        bool truncated = false,
        IReadOnlyList<KeyValuePair<string, string>>? state = null) => new()
        {
            NodeId = NodeId,
            ClassName = "func_door",
            Incoming = incoming ?? [],
            IncomingTruncated = truncated,
            State = state ?? [],
        };

    private void Publish(EntityPanelInfo? info) => _panel.Apply([], info is null ? 0 : 1, info);

    [Fact]
    public void Receives_lists_who_sends_what_to_which_input()
    {
        Publish(Info(
        [
            new EntityIncomingInfo(Button, "Button", "OnPressed", "Open"),
            new EntityIncomingInfo(Zone, "Zone", "OnTrigger", "Close"),
        ]));

        _panel.Arrivals.Rows.ShouldBe(
        [
            new EntityArrivalRow(Button, "Button", "OnPressed", "Open"),
            new EntityArrivalRow(Zone, "Zone", "OnTrigger", "Close"),
        ]);
        _panel.Arrivals.HasRows.ShouldBeTrue();
        _panel.Arrivals.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void An_entity_nothing_is_wired_to_says_so_and_no_entity_says_nothing()
    {
        Publish(Info());

        _panel.Arrivals.HasEntity.ShouldBeTrue();
        _panel.Arrivals.IsEmpty.ShouldBeTrue();

        Publish(null);

        _panel.Arrivals.HasEntity.ShouldBeFalse();
        _panel.Arrivals.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void The_same_arrivals_published_again_leave_the_rows_alone()
    {
        Publish(Info([new EntityIncomingInfo(Button, "Button", "OnPressed", "Open")]));
        EntityArrivalRow first = _panel.Arrivals.Rows.Single();

        // A new list with the same wires, as every snapshot brings.
        Publish(Info([new EntityIncomingInfo(Button, "Button", "OnPressed", "Open")]));

        _panel.Arrivals.Rows.Single().ShouldBeSameAs(first);
    }

    [Fact]
    public void A_capped_list_says_it_is_capped()
    {
        Publish(Info([new EntityIncomingInfo(Button, "Button", "OnPressed", "Open")], truncated: true));

        _panel.Arrivals.IsTruncated.ShouldBeTrue();
        _panel.Arrivals.TruncatedNote.ShouldContain(EntityPanelInfo.MaxIncoming.ToString());
    }

    [Fact]
    public void Now_shows_the_running_state_and_goes_away_when_the_level_stops()
    {
        Publish(Info(state: [new("open", "0"), new("ticks travelled", "3")]));

        _panel.LiveState.HasRows.ShouldBeTrue();
        _panel.LiveState.Rows.Select(row => (row.Name, row.Value))
            .ShouldBe([("open", "0"), ("ticks travelled", "3")]);

        Publish(Info());

        _panel.LiveState.HasRows.ShouldBeFalse();
    }

    [Fact]
    public void A_new_value_updates_its_row_in_place()
    {
        Publish(Info(state: [new("ticks travelled", "3")]));
        EntityStateRowModel row = _panel.LiveState.Rows.Single();
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Publish(Info(state: [new("ticks travelled", "4")]));

        _panel.LiveState.Rows.Single().ShouldBeSameAs(row);
        row.Value.ShouldBe("4");
        changed.ShouldBe([nameof(EntityStateRowModel.Value)]);
    }
}
