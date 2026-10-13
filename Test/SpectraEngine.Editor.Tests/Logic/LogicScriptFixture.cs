using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Collections.Generic;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

// The level the Logic view's scripts were designed on: a button opens a door
// through a keypad by wires, and two scripts do the rest. Main hears a zone
// and opens the door from a module's code, and Beep, inside the button, hears
// the button.
internal static class LogicScriptFixture
{
    public const string Doors = "Scripts/DoorController.luau";
    public const string BeepFile = "Scripts/Beep.luau";

    public static readonly Guid Zone = Id(21);
    public static readonly Guid Button = Id(22);
    public static readonly Guid Keypad = Id(23);
    public static readonly Guid Door = Id(24);
    public static readonly Guid Main = Id(31);
    public static readonly Guid Beep = Id(32);

    public static LogicEntityInfo[] Entities() =>
    [
        Entity(21, "Zone7", "trigger_multiple"),
        Entity(22, "KeyButton", "func_button", Wire("OnPressed", "Keypad", "Trigger")),
        Entity(23, "Keypad", "logic_relay", Wire("OnTrigger", "Door7", "Open")),
        Entity(24, "Door7", "func_door"),
    ];

    public static LogicScriptInfo[] Scripts() =>
    [
        new LogicScriptInfo(Main, "Main", Guid.Empty),
        new LogicScriptInfo(Beep, "Beep", Button),
    ];

    public static LogicGraphInfo Level() => Level(Entities(), Scripts());

    public static LogicGraphInfo Level(LogicEntityInfo[] entities, params LogicScriptInfo[] scripts) =>
        new() { Entities = entities, TotalEntities = entities.Length, Scripts = scripts };

    public static LogicScriptWire Hears(Guid script, Guid entity, string output, string file = Doors, int line = 1) =>
        new() { Script = script, Entity = entity, Port = output, IsSend = false, File = file, Line = line };

    public static LogicScriptWire Sends(Guid script, Guid entity, string input, string file = Doors, int line = 1) =>
        new() { Script = script, Entity = entity, Port = input, IsSend = true, File = file, Line = line };

    public static LogicScriptWiring Running(params LogicScriptWire[] wires) => new() { Wires = wires };

    // What the design shows: three dashed wires.
    public static LogicScriptWiring Designed() => Running(
        Hears(Main, Zone, "OnTrigger", Doors, 42),
        Sends(Main, Door, "Open", Doors, 57),
        Hears(Beep, Button, "OnPressed", BeepFile, 3));

    // The level at a tick, with what each of the scripts' wires has carried.
    public static LogicPlayInfo Playing(
        LogicScriptWiring scripts, long tick = LogicPlayFixture.Tick, params LogicScriptActivity[] carried) => new()
    {
        Tick = tick,
        Time = tick / 60f,
        Scripts = scripts,
        ScriptActivity = carried.Length == scripts.Wires.Count ? carried : new LogicScriptActivity[scripts.Wires.Count],
    };

    public static FrameSnapshot Snapshot(LogicGraphInfo level, LogicPlayInfo? play = null, params Guid[] selected) =>
        LogicPlayFixture.Snapshot(level, play, selected);

    public static LogicGraph Graph(LogicScriptWiring? running, bool showsScripts = true) =>
        LogicGraph.Build(Level(), Catalog, running, showsScripts);

    // The whole level as it is drawn while it runs, laid out with the fake ruler.
    public static LogicScene Arrange(LogicGraph graph, params Guid[] selected)
    {
        var selection = new HashSet<Guid>(selected);
        return LogicLayout.Arrange(
            new LogicScope().Apply(graph, selection),
            selection,
            new LogicLayoutOptions { ShowsState = true },
            new FixedWidthRuler());
    }
}
