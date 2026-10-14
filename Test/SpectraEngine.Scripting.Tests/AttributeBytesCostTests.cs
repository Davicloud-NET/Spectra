using System.Diagnostics;
using System.Globalization;
using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What attributes hold in managed memory, for each node, written down. A
/// measurement and no test: it runs only when asked for, with -explicit only.
/// <see cref="AttributeBytesTests"/> holds the bounds.
/// </summary>
public sealed class AttributeBytesCostTests
{
    private const int Nodes = 4000;

    [Fact(Explicit = true)]
    public void What_attributes_hold_is_written_down()
    {
        var table = new StringBuilder();
        foreach (int count in (int[])[1, 2, 4, 16])
            table.AppendLine(Line($"set by a script, {count} numbers", ByScript(Nodes, Numbers(count))));

        table.AppendLine(Line("set by a script, 16 byte string", ByScript(Nodes, "part:SetAttribute('A1', '0123456789abcdef')")));
        table.AppendLine(Line("set by a script, 1000 byte string", ByScript(Nodes, "part:SetAttribute('A1', string.rep('x', 1000))")));
        table.AppendLine(Line("set by a script, 64 KiB string", ByScript(200, "part:SetAttribute('A1', string.rep('x', 65536))")));

        foreach (int count in (int[])[1, 2, 4, 16])
            table.AppendLine(Line($"built with, {count} numbers", Built(count, text: false)));

        table.AppendLine(Line("built with, 16 byte string", Built(1, text: true)));

        // Once for nothing: the first level runs before the runtime compiled the calls again.
        Time("warm", 1, "local _ = part:GetAttribute('A1')");
        foreach (int count in (int[])[1, 4, 16, NodeAttributes.MostWalked, NodeAttributes.MostWalked + 1, 64])
        {
            table.AppendLine(Time($"get the last of {count} numbers", count, $"local _ = part:GetAttribute('A{count}')"));
            table.AppendLine(Time($"set the last of {count} to what it has", count, $"part:SetAttribute('A{count}', {count})"));
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "attribute-bytes.txt"), table.ToString());
    }

    private static string Numbers(int count)
    {
        var lines = new StringBuilder();
        for (int index = 1; index <= count; index++)
            lines.Append(CultureInfo.InvariantCulture, $"part:SetAttribute('A{index}', {index}) ");

        return lines.ToString();
    }

    // Managed bytes a node gains when a script sets these attributes on it.
    private static double ByScript(int nodes, string statement)
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, $$"""
            local holder = Instance.new("Folder")
            holder.Parent = workspace
            local parts = table.create({{nodes}})
            for index = 1, {{nodes}} do
                local part = Instance.new("Folder")
                part.Parent = holder
                parts[index] = part
            end
            task.wait()
            for index = 1, {{nodes}} do
                local part = parts[index]
                {{statement}}
            end
            task.wait(1000)
            print(#parts)
            """, "Main");
        level.Play().Tick();
        long before = GC.GetTotalMemory(true);
        level.Tick();
        long after = GC.GetTotalMemory(true);

        level.Errors.ShouldBeEmpty();
        return (after - before) / (double)nodes;
    }

    // The time of one statement, after a node got so many number attributes.
    private static string Time(string name, int count, string statement)
    {
        const int Rounds = 2000;
        const int Ticks = 100;
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, $$"""
            local part = Instance.new("Folder")
            part.Parent = workspace
            for index = 1, {{count}} do
                part:SetAttribute("A" .. index, index)
            end
            game:GetService("RunService").Heartbeat:Connect(function()
                for i = 1, {{Rounds}} do
                    {{statement}}
                end
            end)
            """, "Main");
        level.Play().Tick(30);

        double best = double.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            long started = Stopwatch.GetTimestamp();
            level.Tick(Ticks);
            best = Math.Min(best, Stopwatch.GetElapsedTime(started).TotalNanoseconds / (Ticks * Rounds));
        }

        level.Errors.ShouldBeEmpty();
        return string.Create(CultureInfo.InvariantCulture, $"{name,-36} {best,9:0} ns");
    }

    // Managed bytes of one attribute set a node is built with.
    private static double Built(int count, bool text)
    {
        var sets = new AttributeSet[Nodes];
        var pairs = new KeyValuePair<string, MemberValue>[count];
        string[] names = [.. Enumerable.Range(1, count).Select(index => $"A{index}")];

        long before = GC.GetTotalMemory(true);
        for (int node = 0; node < Nodes; node++)
        {
            for (int index = 0; index < count; index++)
            {
                MemberValue value = text ? MemberValue.Of(new string('x', 16)) : MemberValue.Of(index + 0.5);
                pairs[index] = new KeyValuePair<string, MemberValue>(names[index], value);
            }

            AttributeSet.TryMake(pairs, out AttributeSet? made, out _).ShouldBeTrue();
            sets[node] = made;
        }

        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(sets);
        return (after - before) / (double)Nodes;
    }

    private static string Line(string name, double bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{name,-36} {bytes,9:0} bytes a node");
}
