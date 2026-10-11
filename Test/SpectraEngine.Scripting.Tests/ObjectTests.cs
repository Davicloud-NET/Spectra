using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Managed objects in the hands of scripts: a userdata names a slot and a
/// generation, and a handle to something released is an error, never another object.
/// </summary>
public sealed class ObjectTests
{
    private const int DoorTag = 1;

    [Fact]
    public void An_object_reaches_a_script_and_comes_back_as_itself()
    {
        Door door = new("front");
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText("local door = ...\nreturn door, name_of(door)");

        script.Start([LuauValue.Object(DoorTag, door)]).ShouldBe(LuauResume.Finished);

        host.Objects.TryGet(script.Thread.Values, 1, DoorTag, out object? back).ShouldBe(LuauObjectLookup.Found);
        back.ShouldBeSameAs(door);
        script.Thread.Values.GetText(2).ShouldBe("front");
    }

    // One userdata per object at a time, so == and table keys work as they do in Roblox.
    [Fact]
    public void Two_reads_of_one_object_are_equal_in_a_script()
    {
        Door door = new("front");
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText("local a, b, other = ...\nlocal seen = { [a] = true }\nreturn a == b, seen[b] == true, a == other");

        script.Start([LuauValue.Object(DoorTag, door), LuauValue.Object(DoorTag, door), LuauValue.Object(DoorTag, new Door("back"))])
            .ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetBoolean(1).ShouldBeTrue();
        script.Thread.Values.GetBoolean(2).ShouldBeTrue();
        script.Thread.Values.GetBoolean(3).ShouldBeFalse();
        host.Objects.Count.ShouldBe(2);
    }

    [Fact]
    public void An_object_Luau_no_longer_holds_is_let_go()
    {
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText("local a, b = coroutine.yield()\ncoroutine.yield()\na, b = nil, nil\ncoroutine.yield()");
        script.Start().ShouldBe(LuauResume.Yielded);
        script.Thread.Resume([LuauValue.Object(DoorTag, new Door("a")), LuauValue.Object(DoorTag, new Door("b"))])
            .ShouldBe(LuauResume.Yielded);
        host.Collect();
        int held = host.Objects.Count;

        script.Thread.Resume().ShouldBe(LuauResume.Yielded);
        host.Collect();

        held.ShouldBe(2);
        host.Objects.Count.ShouldBe(0);
    }

    [Fact]
    public void A_handle_to_a_released_object_is_an_error_in_the_script()
    {
        Door door = new("front");
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText("local door = ...\ncoroutine.yield()\nreturn pcall(name_of, door)");
        script.Start([LuauValue.Object(DoorTag, door)]).ShouldBe(LuauResume.Yielded);

        host.Objects.Release(door).ShouldBeTrue();
        script.Thread.Resume().ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetText(2).ShouldBe("Scripts/test.luau:3: the door was removed");
    }

    // The slot a released object had goes to the next object. The old handle
    // must not read as that one.
    [Fact]
    public void A_handle_to_a_released_object_never_reads_as_the_object_that_took_its_slot()
    {
        Door first = new("first");
        Door second = new("second");
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText(
            "local old = ...\nlocal new = coroutine.yield()\nlocal ok = pcall(name_of, old)\nreturn ok, name_of(new), old == new");
        script.Start([LuauValue.Object(DoorTag, first)]).ShouldBe(LuauResume.Yielded);

        host.Objects.Release(first).ShouldBeTrue();
        script.Thread.Resume([LuauValue.Object(DoorTag, second)]).ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetText(2).ShouldBe("second");
        script.Thread.Values.GetBoolean(3).ShouldBeFalse();
        host.Objects.Count.ShouldBe(1);
    }

    // Luau clears its record of a userdata before it frees it. A push in
    // between makes a new userdata, and the report about the old one must
    // not let the object go.
    [Fact]
    public void An_object_pushed_again_after_Luau_dropped_it_stays_reachable()
    {
        Door door = new("front");
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText(
            "local door = coroutine.yield()\ndoor = nil\nlocal again = coroutine.yield()\ncoroutine.yield()\nreturn name_of(again)");
        script.Start().ShouldBe(LuauResume.Yielded);
        script.Thread.Resume([LuauValue.Object(DoorTag, door)]).ShouldBe(LuauResume.Yielded);
        Lua.lua_gc(host.Stack.State, LuaGc.Collect, 0);

        script.Thread.Resume([LuauValue.Object(DoorTag, door)]).ShouldBe(LuauResume.Yielded);
        host.Objects.ReleaseCollected();
        script.Thread.Resume().ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetText(1).ShouldBe("front");
        host.Objects.Count.ShouldBe(1);
    }

    [Fact]
    public void Something_else_is_not_an_object_of_the_kind()
    {
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText("return 'door', {}");
        script.Start().ShouldBe(LuauResume.Finished);

        host.Objects.TryGet(script.Thread.Values, 1, DoorTag, out _).ShouldBe(LuauObjectLookup.NotThisKind);
        host.Objects.TryGet(script.Thread.Values, 2, DoorTag, out _).ShouldBe(LuauObjectLookup.NotThisKind);
        host.Objects.TryGet(script.Thread.Values, 3, DoorTag, out _).ShouldBe(LuauObjectLookup.NotThisKind);
    }

    [Fact]
    public void A_script_cannot_change_the_metatable_all_objects_of_a_kind_share()
    {
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText(
            "local door = ...\nlocal shared = getmetatable(door)\nreturn pcall(function() shared.__index = nil end), pcall(function() shared.methods.added = 1 end)");

        script.Start([LuauValue.Object(DoorTag, new Door("front"))]).ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetBoolean(2).ShouldBeFalse();
    }

    [Fact]
    public void Many_objects_come_and_go_and_none_is_left()
    {
        using LuauHost host = OpenWithDoors(out _);
        LuauScript script = host.LoadText("while true do coroutine.yield() end");
        script.Start().ShouldBe(LuauResume.Yielded);

        for (int i = 0; i < 5000; i++)
            script.Thread.Resume([LuauValue.Object(DoorTag, new Door("door"))]).ShouldBe(LuauResume.Yielded);

        // The last door is still on the thread's stack.
        script.Thread.Cancel().ShouldBeTrue();
        host.Collect();

        host.Objects.Count.ShouldBe(0);
    }

    // A host whose Door objects have one function, name_of(door), which
    // fails for a door that was released.
    private static LuauHost OpenWithDoors(out RecordingSink sink) => TestHost.Open(out sink, fresh =>
    {
        LuauStack stack = fresh.Stack;
        stack.NewTable().ShouldBeTrue();
        stack.Push("Door").ShouldBeTrue();
        stack.SetField(-2, "__type"u8).ShouldBeTrue();
        stack.NewTable().ShouldBeTrue();
        stack.SetField(-2, "methods"u8).ShouldBeTrue();
        fresh.Objects.SetMetatable(stack, DoorTag).ShouldBeTrue();

        fresh.SetGlobal("name_of"u8, call => fresh.Objects.TryGet(call, 1, DoorTag, out object? found) switch
        {
            LuauObjectLookup.Found when found is Door door => call.Push(door.Name) ? 1 : LuauStack.Raise,
            LuauObjectLookup.Released => call.Fail("the door was removed"),
            _ => call.Fail("not a door"),
        });
    });

    private sealed record Door(string Name);
}
