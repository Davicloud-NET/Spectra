namespace SpectraEngine.Core.Entities;

/// <summary>
/// What travels with one input: who started the chain, who sent this message,
/// and the argument it carries. Passed by ref, so every entity resolved from
/// one target name gets the same context.
/// </summary>
public struct EntityInputContext
{
    public EntityInputContext(Entity? activator, Entity? caller, string? parameter)
    {
        Activator = activator;
        Caller = caller;
        Parameter = parameter ?? "";
    }

    /// <summary>Whoever set this cascade going, carried unchanged down every hop.</summary>
    public Entity? Activator { get; }

    /// <summary>The entity whose output produced this message. Changes at every hop.</summary>
    public Entity? Caller { get; }

    /// <summary>The argument, in wire form. Never null; empty means "none".</summary>
    public string Parameter { get; }
}
