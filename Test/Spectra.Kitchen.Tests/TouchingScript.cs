using System;

namespace Spectra.Kitchen.Tests;

// A script that listens for the player's touch on the pad it sits under and
// on what is around it, and prints what it is told and what it is refused.
// The level starts the player above the pad and inside a zone. Every line
// begins with the same word, so a test finds them among the rest.
internal static class TouchingScript
{
    public const string Path = "Scripts/touches.luau";

    // The pad the player lands on, which the script sits under, and the
    // zone the player starts in.
    public const string Pad = "Pad";
    public const string Zone = "Gate";

    // What every line of the script begins with.
    public const string Mark = "Touches | ";

    // It waits for the next tick after every third line, as the asking
    // script does: all scripts of a level share a hundred lines a tick.
    public const string Text = """
        local character = game:GetService("Players").LocalPlayer.Character
        local pad, gate = script.Parent, workspace.Gate
        local floor, doorway, plank = workspace.Floor, workspace.Doorway, workspace.Shelf.Plank
        local prop, door, trigger, sun = workspace.Props.Prop, workspace.Door, workspace.DoorTrigger, workspace.Sun
        local lines = 0

        local function say(what, answer)
        	print("Touches | " .. what .. " | " .. tostring(answer))
        	lines += 1
        	if lines % 3 == 0 then
        		task.wait()
        	end
        end

        -- A node goes by the name it had when the level started: the asking
        -- scripts rename what they sit under.
        local function listen(node)
        	local name = node.Name
        	local ok, why = pcall(function()
        		node.Touched:Connect(function(hit)
        			print("Touches | " .. name .. " touched | " .. tostring(hit == character))
        		end)
        		node.TouchEnded:Connect(function(hit)
        			print("Touches | " .. name .. " left | " .. tostring(hit == character))
        		end)
        	end)
        	say("listen to " .. name, if ok then "done" else why)
        end

        for _, node in { pad, gate, floor, doorway, plank, prop, door, trigger } do
        	listen(node)
        end
        say("a light", select(2, pcall(function() return sun.Touched end)))
        say("one signal", `{pad.Touched == pad.Touched} {typeof(pad.Touched)} {pad.TouchEnded}`)

        task.wait(0.75)
        gate.CanTouch = false
        say("the zone still notices", gate.CanTouch)
        task.wait(0.1)
        say("listened", "to everything")
        """;

    private const string Refused =
        " cannot tell what touches it: it is a world brush, fused into the level, and only parts notice a " +
        "touch while a level plays. Convert the brush to a part.";

    // What a level with the script prints after the mark, in order: what
    // was asked, and how the answer ends. An answer that is an error has
    // the line that raised it in front. The player starts inside the zone,
    // falls onto the pad and stays there.
    public static readonly (string What, string Answer)[] Lines =
    [
        ("listen to Pad", "done"),
        ("listen to Gate", "done"),
        ("listen to Floor", "Floor" + Refused),
        ("Gate touched", "true"),
        ("listen to Doorway", "Doorway" + Refused),
        ("listen to Plank", "Plank" + Refused),
        ("listen to Prop", "done"),
        ("listen to Door", "done"),
        ("listen to DoorTrigger", "done"),
        ("a light", "Touched is not a valid member of DirectionalLight \"Sun\""),
        ("one signal", "true RBXScriptSignal Signal TouchEnded"),
        ("Pad touched", "true"),
        ("the zone still notices", "false"),
        ("Gate left", "true"),
        ("listened", "to everything"),
    ];

    // A line of the level's output as the script said it: without its kind,
    // its place and the mark.
    public static string Said(string line) => line[(line.IndexOf(Mark, StringComparison.Ordinal) + Mark.Length)..];
}
