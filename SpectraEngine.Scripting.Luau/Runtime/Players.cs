using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The players of the level. A level has one, the player who walks it, or none.</summary>
[ScriptClass("Players")]
internal sealed partial class Players : ScriptObject
{
    private readonly PlayerWrites _door;
    private readonly ScriptPlayer _player;

    public Players(PlayerWrites door)
    {
        _door = door;
        _player = new ScriptPlayer(door);
    }

    public override string Name => "Players";

    /// <summary>The player who walks the level. Nil in a level played with nothing walking.</summary>
    // Asked on every read: a host gives the level its player after the
    // level has started.
    [ScriptProperty]
    public ScriptPlayer? LocalPlayer => _door.Player is null ? null : _player;

    // The one character, for what hands it to a script without being asked
    // for it, as a touch does.
    public ScriptCharacter Body => _player.Body;
}
