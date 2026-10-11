using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>Values on their way across: each kind, tables, plain userdata, and what it costs.</summary>
public sealed unsafe class ValueTests
{
    private const int PointTag = 5;

    [Fact]
    public void Each_kind_of_value_goes_into_a_script_and_comes_back()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return ...");

        LuauResume outcome = script.Start([LuauValue.Nil, true, 2.5, "türkis", new Vector3(1, 2, 3)]);

        outcome.ShouldBe(LuauResume.Finished);
        LuauStack values = script.Thread.Values;
        script.Thread.ValueCount.ShouldBe(5);
        values.TypeOf(1).ShouldBe(LuauType.Nil);
        values.TypeOf(2).ShouldBe(LuauType.Boolean);
        values.GetBoolean(2).ShouldBeTrue();
        values.TryGetNumber(3, out double number).ShouldBeTrue();
        number.ShouldBe(2.5);
        values.GetText(4).ShouldBe("türkis");
        values.TryGetVector(5, out Vector3 vector).ShouldBeTrue();
        vector.ShouldBe(new Vector3(1, 2, 3));
    }

    [Fact]
    public void A_vector_is_Luaus_own_vector_type()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("local v = ...\nreturn typeof(v), v.x + v.y + v.z, v * 2");

        script.Start([new Vector3(1, 2, 3)]).ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetText(1).ShouldBe("vector");
        script.Thread.Values.TryGetNumber(2, out double sum).ShouldBeTrue();
        sum.ShouldBe(6);
        script.Thread.Values.TryGetVector(3, out Vector3 doubled).ShouldBeTrue();
        doubled.ShouldBe(new Vector3(2, 4, 6));
    }

    [Fact]
    public void A_read_of_the_wrong_kind_says_so()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return 'door', {}");
        script.Start().ShouldBe(LuauResume.Finished);
        LuauStack values = script.Thread.Values;

        values.TryGetNumber(1, out _).ShouldBeFalse();
        values.TryGetVector(1, out _).ShouldBeFalse();
        values.GetText(2).ShouldBeNull();
        values.TypeOf(3).ShouldBe(LuauType.None);
        values.IsNil(3).ShouldBeTrue();
    }

    [Fact]
    public void A_string_is_read_as_bytes_where_Luau_keeps_it()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return 'hinge'");
        script.Start().ShouldBe(LuauResume.Finished);

        bool isText = script.Thread.Values.TryGetUtf8(1, out ReadOnlySpan<byte> text);

        isText.ShouldBeTrue();
        text.SequenceEqual("hinge"u8).ShouldBeTrue();
    }

    [Fact]
    public void A_table_is_made_filled_and_read_by_a_script()
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.Push(20).ShouldBeTrue();
            stack.SetField(-2, "speed"u8).ShouldBeTrue();
            stack.Push("first").ShouldBeTrue();
            stack.SetElement(-2, 1).ShouldBeTrue();
            stack.SetGlobal("settings"u8).ShouldBeTrue();
        });

        LuauScript script = host.LoadText("return settings.speed, settings[1], #settings");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.TryGetNumber(1, out double speed).ShouldBeTrue();
        speed.ShouldBe(20);
        script.Thread.Values.GetText(2).ShouldBe("first");
        script.Thread.Values.TryGetNumber(3, out double length).ShouldBeTrue();
        length.ShouldBe(1);
    }

    [Fact]
    public void A_table_a_script_made_is_read_and_walked()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return { 'a', 'b', speed = 20 }");
        script.Start().ShouldBe(LuauResume.Finished);
        LuauStack values = script.Thread.Values;

        values.GetField(1, "speed"u8).ShouldBeTrue();
        values.TryGetNumber(-1, out double speed).ShouldBeTrue();
        values.Pop();
        values.GetElement(1, 2).ShouldBeTrue();
        string? second = values.GetText(-1);
        values.Pop();

        List<string> keys = [];
        int cursor = 0;
        while (values.Next(1, ref cursor) == LuauWalk.Pair)
        {
            keys.Add(values.TypeOf(-2) == LuauType.Number ? "number" : values.GetText(-2) ?? "?");
            values.Pop(2);
        }

        speed.ShouldBe(20);
        second.ShouldBe("b");
        values.Length(1).ShouldBe(2);
        keys.ShouldBe(["number", "number", "speed"], ignoreOrder: true);
        values.Count.ShouldBe(1);
    }

    [Fact]
    public void A_frozen_table_refuses_the_host_and_the_script()
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.NewTable().ShouldBeTrue();
            stack.Freeze(-1).ShouldBeTrue();
            stack.Push(1).ShouldBeTrue();
            stack.SetField(-2, "speed"u8).ShouldBeFalse();
            stack.GetText(-1).ShouldNotBeNull().ShouldContain("readonly");
            stack.Pop();
            stack.SetField(-2, "inner"u8).ShouldBeTrue();
            stack.SetGlobal("settings"u8).ShouldBeTrue();
        });

        LuauScript script = host.LoadText("return pcall(function() settings.inner.speed = 2 end)");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
    }

    // What a CFrame will be: a value that lives in the userdata itself.
    [Fact]
    public void A_userdata_carries_a_value_of_its_own_and_gets_the_metatable_of_its_tag()
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.Push("Point").ShouldBeTrue();
            stack.SetField(-2, "__type"u8).ShouldBeTrue();
            stack.PushFunction(&PointSum, "sum"u8).ShouldBeTrue();
            stack.SetField(-2, "__len"u8).ShouldBeTrue();
            stack.SetUserdataMetatable(PointTag).ShouldBeTrue();

            fresh.SetGlobal("point"u8, call =>
            {
                call.TryGetNumber(1, out double x).ShouldBeTrue();
                call.TryGetNumber(2, out double y).ShouldBeTrue();
                return call.PushUserdata(PointTag, new Point(x, y)) ? 1 : LuauStack.Raise;
            });
        });

        LuauScript script = host.LoadText("local p = point(3, 4)\nreturn typeof(p), #p, p");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetText(1).ShouldBe("Point");
        script.Thread.Values.TryGetNumber(2, out double sum).ShouldBeTrue();
        sum.ShouldBe(7);
        script.Thread.Values.TryGetUserdata(3, PointTag, out Point point).ShouldBeTrue();
        point.ShouldBe(new Point(3, 4));
        script.Thread.Values.TryGetUserdata(3, PointTag + 1, out Point _).ShouldBeFalse();
    }

    [Fact]
    public void A_tag_takes_one_metatable_once()
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.SetUserdataMetatable(PointTag).ShouldBeTrue();

            stack.NewTable().ShouldBeTrue();
            stack.SetUserdataMetatable(PointTag).ShouldBeFalse();
            stack.GetText(-1).ShouldBe("the userdata tag already has a metatable");
            stack.Pop();
            stack.Count.ShouldBe(0);
        });
    }

    [Fact]
    public void A_negative_index_past_the_bottom_is_an_exception_and_not_a_read_below_the_stack()
    {
        using LuauHost host = TestHost.Open(out _);
        host.Stack.Push(1).ShouldBeTrue();

        Should.Throw<ArgumentOutOfRangeException>(() => host.Stack.TypeOf(-2));
        host.Stack.GetField(-2, "speed"u8).ShouldBeFalse();
        host.Stack.GetText(-1).ShouldBe("no value at that stack index");
    }

    // The calls a tick makes over and over: a host function with numbers, a
    // resume with a number, a kept function started with a vector.
    [Fact]
    public void The_hot_paths_allocate_nothing()
    {
        LuauFunction? kept = null;
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            fresh.Stack.PushFunction(&Add, "add"u8).ShouldBeTrue();
            fresh.Stack.SetGlobal("add"u8).ShouldBeTrue();
            fresh.SetGlobal("keep"u8, stack =>
            {
                kept = fresh.KeepFunction(stack, 1);
                return 0;
            });
        });
        LuauScript script = host.LoadText(
            "keep(function(v) return add(v.x, v.y) end)\nlocal total = 0\nwhile true do total = add(total, coroutine.yield(total)) end");
        script.Start().ShouldBe(LuauResume.Yielded);
        kept.ShouldNotBeNull();

        // Twice: the first round pays for what is made once.
        long allocated = 0;
        for (int round = 0; round < 2; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                if (script.Thread.Resume([1.5]) != LuauResume.Yielded || kept.Start([new Vector3(1, 2, 3)]) != LuauResume.Finished)
                    throw new InvalidOperationException("a round did not go as it should");
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        allocated.ShouldBe(0);
        script.Thread.Values.TryGetNumber(1, out double total).ShouldBeTrue();
        total.ShouldBe(3000);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Add(nint state)
    {
        LuauStack stack = new(state);
        try
        {
            if (!stack.TryGetNumber(1, out double left) || !stack.TryGetNumber(2, out double right))
                return stack.Fail("add takes two numbers");

            return stack.Push(left + right) ? 1 : LuauStack.Raise;
        }
        catch (Exception e)
        {
            return stack.Fail(e);
        }
    }

    // __len of a Point: the sum of its two numbers, to show a metamethod reading the block.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PointSum(nint state)
    {
        LuauStack stack = new(state);
        try
        {
            if (!stack.TryGetUserdata(1, PointTag, out Point point))
                return stack.Fail("not a Point");

            return stack.Push(point.X + point.Y) ? 1 : LuauStack.Raise;
        }
        catch (Exception e)
        {
            return stack.Fail(e);
        }
    }

    private readonly record struct Point(double X, double Y);
}
