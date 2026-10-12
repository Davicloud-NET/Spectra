using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The one way a script changes the player while a level plays. A change to
/// how the player moves records the value it had in the level's journal
/// first, so stopping the level puts it back. Where the player is and how
/// fast it moves need no record: the next Play spawns it again.
/// </summary>
public sealed class PlayerWrites
{
    /// <summary>The fastest a script can make the player walk, or throw it, in units a second.</summary>
    // Past any game, and far below where a speed times itself is no number.
    public const float MaxSpeed = 10_000f;

    /// <summary>The highest jump a script can give the player, in units.</summary>
    public const float MaxJumpHeight = 10_000f;

    private readonly EntityWorld _world;

    /// <summary>The writes of the scripts of one level to its player.</summary>
    public PlayerWrites(EntityWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <summary>
    /// The player, or null when nothing walks the level. Read again on every
    /// use: a host gives the level its player after the level has started.
    /// </summary>
    public IPlayerPresence? Player => _world.Player is { IsPresent: true } player ? player : null;

    /// <summary>Sets how fast the player walks, in units a second.</summary>
    /// <param name="refusal">Why nothing was written, as the script's error says it.</param>
    /// <returns>False when the write was refused. The player is as it was then.</returns>
    public bool TrySetWalkSpeed(float value, [NotNullWhen(false)] out string? refusal)
    {
        if (!TryFindPlayer(out IPlayerPresence? player, out refusal))
            return false;

        if (!IsFrom0To(MaxSpeed, value))
        {
            refusal = OutOfRange("WalkSpeed", value, MaxSpeed);
            return false;
        }

        _world.Journal.Record(player.Tuning, TuningJournalKind.WalkSpeed);
        player.Tuning.WalkSpeed = value;
        return true;
    }

    /// <summary>Sets how high the player jumps, in units.</summary>
    /// <param name="refusal">Why nothing was written, as the script's error says it.</param>
    /// <returns>False when the write was refused. The player is as it was then.</returns>
    public bool TrySetJumpHeight(float value, [NotNullWhen(false)] out string? refusal)
    {
        if (!TryFindPlayer(out IPlayerPresence? player, out refusal))
            return false;

        if (!IsFrom0To(MaxJumpHeight, value))
        {
            refusal = OutOfRange("JumpHeight", value, MaxJumpHeight);
            return false;
        }

        _world.Journal.Record(player.Tuning, TuningJournalKind.JumpHeight);
        player.Tuning.JumpHeight = value;
        return true;
    }

    /// <summary>
    /// Gives the player a velocity, in units a second. The next tick carries
    /// on from it, and one that points up takes the player off the ground.
    /// </summary>
    /// <param name="refusal">Why nothing was written, as the script's error says it.</param>
    /// <returns>False when the write was refused. The player is as it was then.</returns>
    public bool TrySetVelocity(Vector3 value, [NotNullWhen(false)] out string? refusal)
    {
        if (!TryFindPlayer(out IPlayerPresence? player, out refusal))
            return false;

        // A length that is no number fails the comparison too.
        if (!(value.Length() <= MaxSpeed))
        {
            refusal = string.Create(
                CultureInfo.InvariantCulture,
                $"Velocity cannot be ({value.X}, {value.Y}, {value.Z}): it takes three numbers and no more than {MaxSpeed} units a second.");
            return false;
        }

        player.SetVelocity(value);
        return true;
    }

    /// <summary>Puts the player at rest with its feet at a point, looking the way it did.</summary>
    /// <param name="refusal">Why the player was not moved, as the script's error says it.</param>
    /// <returns>False when the move was refused. The player is as it was then.</returns>
    public bool TryTeleport(Vector3 feet, [NotNullWhen(false)] out string? refusal) =>
        TryTeleport(feet, yaw: null, out refusal);

    /// <summary>
    /// Puts the player at rest with its feet at a frame's position, looking
    /// along the frame's +Z. A frame that looks straight up or down leaves
    /// the player looking the way it did.
    /// </summary>
    /// <param name="refusal">Why the player was not moved, as the script's error says it.</param>
    /// <returns>False when the move was refused. The player is as it was then.</returns>
    public bool TryTeleport(CFrame frame, [NotNullWhen(false)] out string? refusal) =>
        TryTeleport(frame.Position, CharacterFacing.YawOf(in frame), out refusal);

    /// <summary>Sends the player back to where it started, as falling out of the level does.</summary>
    /// <param name="refusal">Why the player was not moved, as the script's error says it.</param>
    /// <returns>False when the move was refused. The player is as it was then.</returns>
    public bool TryRespawn([NotNullWhen(false)] out string? refusal)
    {
        if (!TryFindPlayer(out IPlayerPresence? player, out refusal))
            return false;

        player.Respawn();
        return true;
    }

    private bool TryTeleport(Vector3 feet, float? yaw, [NotNullWhen(false)] out string? refusal)
    {
        if (!TryFindPlayer(out IPlayerPresence? player, out refusal))
            return false;

        if (!float.IsFinite(feet.X) || !float.IsFinite(feet.Y) || !float.IsFinite(feet.Z))
        {
            refusal = "The player cannot be put there: a position must be three numbers, with none of them infinite.";
            return false;
        }

        player.Teleport(feet, yaw);
        return true;
    }

    private bool TryFindPlayer(
        [NotNullWhen(true)] out IPlayerPresence? player, [NotNullWhen(false)] out string? refusal)
    {
        player = Player;
        if (!_world.IsRunningEntities)
        {
            refusal = "The player cannot be changed: the level has stopped.";
            return false;
        }

        if (player is null)
        {
            refusal = "The player cannot be changed: nothing walks the level.";
            return false;
        }

        refusal = null;
        return true;
    }

    // False for what is no number, too.
    private static bool IsFrom0To(float most, float value) => value >= 0f && value <= most;

    private static string OutOfRange(string member, float value, float most) => string.Create(
        CultureInfo.InvariantCulture, $"{member} cannot be {value}: it takes a number from 0 to {most}.");
}
