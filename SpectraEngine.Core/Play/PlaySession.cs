using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Diagnostics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Play;

/// <summary>
/// A level being played: what starts and stops, in which order, and what one
/// fixed tick runs. Needs no window, camera, input or renderer, so a server or
/// a test can play a level. Render thread only.
/// </summary>
public sealed class PlaySession
{
    // A start sits on the floor. The feet go just above it and the first
    // tick's ground snap settles them.
    private const float StartLift = 0.05f;

    /// <summary>How far from the eye the character can use something, in units.</summary>
    public const float UseReach = 2f;

    /// <summary>The input a use sends to the entity it reaches.</summary>
    public const string UseInput = "Use";

    private readonly SceneManager _sceneManager;
    private bool _warnedNoStart;

    /// <summary>Builds a session over a scene manager's scene, entities and physics.</summary>
    /// <param name="sceneManager">Owns the scene, the entity world and the physics backend.</param>
    /// <param name="character">Who walks the level, or null when nothing can be played.</param>
    public PlaySession(SceneManager sceneManager, CharacterSimulation? character = null)
    {
        ArgumentNullException.ThrowIfNull(sceneManager);

        _sceneManager = sceneManager;
        Character = character;
    }

    /// <summary>Who walks the level, or null when nothing can be played.</summary>
    public CharacterSimulation? Character { get; }

    /// <summary>Times the physics step, or null to leave it unmeasured.</summary>
    public FrameProfiler? Profiler { get; init; }

    /// <summary>Where a level with no player start is reported. Nowhere by default.</summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;

    /// <summary>Whether a level is being played.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Starts the entities, puts the character at the level's first player
    /// start and gives it to them as their player. A level with no start uses
    /// the scene manager's spawn. Does nothing without a character or while
    /// already playing.
    /// </summary>
    // A host with an editor suspends it first: a spawn may fire outputs, which
    // must not run with a drag open.
    public void Enter()
    {
        if (IsActive || Character is not { } character)
            return;

        // Entities first: one of them says where the character starts.
        _sceneManager.StartEntityWorld();
        EntityWorld? entities = _sceneManager.EntityWorld;

        ResolveSpawn(character, entities);
        character.Spawn();

        // The world holds the presence, so a map loaded during play drops both.
        if (entities is not null)
            entities.Player = character;

        IsActive = true;
    }

    /// <summary>
    /// Stops the entities, which puts back every node they moved. Does nothing
    /// unless a level is being played.
    /// </summary>
    // A host with an editor resumes it after: OnRemove must run before the
    // editor takes the scene back.
    public void Exit()
    {
        if (!IsActive)
            return;

        _sceneManager.StopEntityWorld();
        IsActive = false;
    }

    /// <summary>
    /// Runs one fixed tick: entities, physics, then the character. Physics
    /// steps outside play too. A use pressed on this tick reaches its entity
    /// on the next one.
    /// </summary>
    /// <param name="fixedDt">The fixed step, never a frame delta.</param>
    /// <param name="command">What the character is asked to do. Unused outside play.</param>
    public PlayTickResult Tick(float fixedDt, in CharacterCommand command)
    {
        // Entities first: one that moves a platform must decide before the
        // step resolves against it. Null outside play, and once a map has
        // been loaded during play.
        _sceneManager.EntityWorld?.Tick(fixedDt);

        IScenePhysics physics = _sceneManager.Physics;
        physics.PushKinematicTargets(fixedDt);
        Step(physics, fixedDt);

        // Here, not after the last tick of a frame: the next step overwrites
        // the event buffers.
        physics.DrainEvents();

        if (!IsActive || Character is not { } character)
            return default;

        // Before the character tick, which overwrites the previous buttons.
        if (IsUsePressed(character.State, in command))
            Use(character, in command);

        // After the step, so the character sees this tick's kinematic poses
        // and rides a moving platform.
        Vector3 previous = character.State.Position;
        bool respawned = character.Tick(in command, fixedDt);
        return new PlayTickResult(previous, respawned);
    }

    // Read on every Play, since the start may have moved. Only the spot and
    // the yaw are kept, so nothing here outlives the entity world.
    private void ResolveSpawn(CharacterSimulation character, EntityWorld? entities)
    {
        if (FirstPlayerStart(entities) is { } start)
        {
            character.SpawnPosition = start.WorldPosition + new Vector3(0f, StartLift, 0f);
            character.SpawnYaw = FacingYaw(start);
            return;
        }

        Vector3 spawn = _sceneManager.PlayerSpawn;
        character.SpawnPosition = spawn;
        character.SpawnYaw = _sceneManager.PlayerSpawnYaw;

        if (_warnedNoStart)
            return;

        _warnedNoStart = true;
        Logger.LogWarning(
            "The level has no player start, so the character starts at ({X:0.##}, {Y:0.##}, {Z:0.##}). " +
            "Insert a player start to choose the spot.",
            spawn.X, spawn.Y, spawn.Z);
    }

    private static SceneNode? FirstPlayerStart(EntityWorld? entities)
    {
        if (entities is null)
            return null;

        IReadOnlyList<Entity> all = entities.Entities;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is IPlayerStart)
                return all[i].Node;
        }

        return null;
    }

    // The node's local +Z, the axis a light shines along. A start that points
    // straight up or down has no heading and gets yaw zero.
    private static float FacingYaw(SceneNode start)
    {
        Matrix4x4 world = start.WorldMatrix;
        float x = world.M31;
        float z = world.M33;
        return (x * x) + (z * z) > 1e-8f ? MathF.Atan2(z, x) : 0f;
    }

    // The edge comes from the state: one command is replayed for every tick
    // of a frame, and a held button must use once.
    private static bool IsUsePressed(in CharacterState state, in CharacterCommand command) =>
        (command.Buttons & CharacterButtons.Use) != 0 &&
        (state.PrevButtons & CharacterButtons.Use) == 0;

    // Aimed by the command, not by a camera: a server and a replay have no view.
    private void Use(CharacterSimulation character, in CharacterCommand command)
    {
        // Null once a map has been loaded during play.
        if (_sceneManager.EntityWorld is not { } entities)
            return;

        Vector3 eye = character.State.Position + new Vector3(0f, character.Tuning.EyeHeight, 0f);
        var ray = new Ray3(eye, ViewDirection(in command));

        // The default filter wants the query flag, which a trigger volume has
        // off, so a volume does not shield what is behind it.
        if (!entities.Scene.RaycastGameplay(in ray, out GameplayRayHit hit, UseReach) ||
            hit.Node is not { } node)
        {
            return;
        }

        // No activator: the player is not an entity.
        if (entities.TryFindOwner(node, out Entity? owner))
            entities.QueueInput(owner, UseInput);
    }

    // The mover's forward axis, tilted by the pitch.
    private static Vector3 ViewDirection(in CharacterCommand command)
    {
        float cosPitch = MathF.Cos(command.Pitch);
        return new Vector3(
            MathF.Cos(command.Yaw) * cosPitch,
            MathF.Sin(command.Pitch),
            MathF.Sin(command.Yaw) * cosPitch);
    }

    private void Step(IScenePhysics physics, float fixedDt)
    {
        if (Profiler is not { } profiler)
        {
            physics.Step(fixedDt);
            return;
        }

        using (profiler.Measure(FramePhase.Physics))
            physics.Step(fixedDt);
    }
}
