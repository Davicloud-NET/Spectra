using System;
using System.Numerics;
using SpectraEngine.Core.Diagnostics;
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
    private readonly SceneManager _sceneManager;

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

    /// <summary>Whether a level is being played.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Puts the character at its spawn, starts the entities and gives them the
    /// character as their player. Does nothing without a character or while
    /// already playing.
    /// </summary>
    // A host with an editor suspends it first: a spawn may fire outputs, which
    // must not run with a drag open.
    public void Enter()
    {
        if (IsActive || Character is not { } character)
            return;

        character.Spawn();
        _sceneManager.StartEntityWorld();

        // The world holds the presence, so a map loaded during play drops both.
        if (_sceneManager.EntityWorld is { } entities)
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
    /// steps outside play too.
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

        // After the step, so the character sees this tick's kinematic poses
        // and rides a moving platform.
        Vector3 previous = character.State.Position;
        bool respawned = character.Tick(in command, fixedDt);
        return new PlayTickResult(previous, respawned);
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
