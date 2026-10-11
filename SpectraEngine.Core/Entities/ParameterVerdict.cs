namespace SpectraEngine.Core.Entities;

/// <summary>
/// What becomes of the parameter a wire was authored with, as far as the
/// schemas can tell. Nothing but <see cref="Fine"/> stops the wire: the
/// input is still sent.
/// </summary>
public enum ParameterVerdict
{
    /// <summary>Nothing wrong that the schemas can see.</summary>
    Fine,

    /// <summary>The output sends a value of its own, which takes the parameter's place.</summary>
    Replaced,

    /// <summary>The input takes no parameter, so this one is ignored.</summary>
    NotTaken,

    /// <summary>The input needs a parameter and the wire has none.</summary>
    Missing,

    /// <summary>The input cannot read the parameter as the type it takes.</summary>
    Unreadable,
}
