using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;

using static SpectraEngine.Editor.Tests.Logic.LogicScriptFixture;

namespace SpectraEngine.Editor.Render.Tests;

// The states of the Logic view that show scripts, as the design drew them:
// the level being edited with the door selected, and the same level at tick
// 1204, a moment after Main opened the door.
internal static class LogicScriptSheets
{
    private const long Tick = 1204;

    // Near the selected door, as the editing design shows it.
    public static void Editing(LogicViewModel model) => model.Apply(Snapshot(Level(), null, Door));

    // The whole level running, the door selected.
    public static void Playing(LogicViewModel model)
    {
        model.Mode = LogicScopeMode.WholeLevel;
        model.Apply(Snapshot(Level(), Play(Designed()), Door));
    }

    // The same near the selected door, two steps out.
    public static void PlayingNear(LogicViewModel model) => model.Apply(Snapshot(Level(), Play(Designed()), Door));

    // The same with the Scripts key off.
    public static void PlayingWithScriptsOff(LogicViewModel model)
    {
        model.ShowsScripts = false;
        Playing(model);
    }

    // Scripts that hear and send a lot, from files with long names: two on
    // one output, one that hears an entity and sends to it, and an input
    // sent from three lines.
    public static void PlayingBusy(LogicViewModel model)
    {
        const string longFile = "Scripts/TheControllerOfEveryDoorOnTheUpperFloor.luau";
        LogicScriptWiring wiring = Running(
            Hears(Main, Zone, "OnTrigger", longFile, 1042),
            Hears(Main, Button, "OnPressed", Doors, 9),
            Hears(Beep, Button, "OnPressed", BeepFile, 3),
            Sends(Main, Door, "Open", Doors, 57),
            Sends(Main, Door, "Open", Doors, 80),
            Sends(Main, Door, "Open", longFile, 4),
            Sends(Main, Door, "Close", Doors, 61),
            Hears(Beep, Door, "OnFullyOpen", BeepFile, 4),
            Sends(Beep, Door, "Close", BeepFile, 5),
            Sends(Beep, Keypad, "Toggle", BeepFile, 8));

        var carried = new LogicScriptActivity[wiring.Wires.Count];
        carried[0] = new LogicScriptActivity(3, Tick);
        carried[6] = new LogicScriptActivity(1, Tick - 2);

        model.Mode = LogicScopeMode.WholeLevel;
        model.Apply(Snapshot(Level(), Play(wiring, carried)));
    }

    private static LogicPlayInfo Play(LogicScriptWiring wiring) => Play(
        wiring,
        new LogicScriptActivity(1, Tick - 60),
        new LogicScriptActivity(1, Tick),
        new LogicScriptActivity(2, Tick - 60));

    private static LogicPlayInfo Play(LogicScriptWiring wiring, params LogicScriptActivity[] carried) => new()
    {
        Tick = Tick,
        Time = Tick / 60f,
        Wires = [new LogicWireActivity { NodeId = Button, Wire = 0, Fired = 2, LastFiredTick = Tick - 60 }],
        States =
        [
            new(Zone, "touched", "yes"),
            new(Button, "state", "in"),
            new(Keypad, "fired", "2 times"),
            new(Door, "opening", "9 of 39 ticks"),
        ],
        Recent =
        [
            Heard(1, 1198, "KeyButton.OnPressed", Beep, (BeepFile, 3)),
            Heard(2, Tick, "Zone7.OnTrigger", Main, (Doors, 42)),
            Sent(3, Tick, Main, "Door7.Open", (Doors, 57)),
        ],
        Scripts = wiring,
        ScriptActivity = carried,
    };
}
