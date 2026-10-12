using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A player of the level. What walks the level is its Character.</summary>
// One for a play session, so the two a script gets are equal.
[ScriptClass("Player")]
internal sealed partial class ScriptPlayer : ScriptObject
{
    private readonly PlayerWrites _door;
    private readonly ScriptCharacter _character;

    public ScriptPlayer(PlayerWrites door)
    {
        _door = door;
        _character = new ScriptCharacter(door);
    }

    /// <summary>The player's name. A level has one player, and it is called Player.</summary>
    [ScriptProperty]
    public override string Name => "Player";

    /// <summary>The player's body in the level. Nil while the player has none.</summary>
    [ScriptProperty]
    public ScriptCharacter? Character => _door.Player is null ? null : _character;
}
