using SpectraEngine.Core.Entities;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What the Sends section says under a wire whose parameter will not do what
/// it looks like it does.
/// </summary>
public static class WireParameterText
{
    /// <summary>
    /// The warning for <paramref name="verdict"/>, or empty when there is
    /// nothing to say. No full stop, like the line about the target.
    /// </summary>
    /// <param name="verdict">What becomes of the wire's parameter.</param>
    /// <param name="wire">The wire as it is typed.</param>
    /// <param name="taken">What the wire's input takes.</param>
    public static string Warning(ParameterVerdict verdict, in EntityConnection wire, ParameterType taken) =>
        verdict switch
        {
            ParameterVerdict.Replaced => $"{wire.Output} sends its own value, so this parameter is not used",
            ParameterVerdict.NotTaken => $"{wire.Input} takes no parameter",
            ParameterVerdict.Missing => $"{wire.Input} needs {Wanted(taken.ValueType)}",
            ParameterVerdict.Unreadable =>
                $"{wire.Input} takes {Wanted(taken.ValueType)}, not '{wire.Parameter}'",
            _ => string.Empty,
        };

    // What to type, in the words a person would use for it.
    private static string Wanted(KeyvalueType type) => type switch
    {
        KeyvalueType.Bool => "0 or 1",
        KeyvalueType.Int => "a whole number",
        KeyvalueType.Float => "a number",
        KeyvalueType.Vec2 => "two numbers",
        KeyvalueType.Vec3 or KeyvalueType.Color or KeyvalueType.Angles => "three numbers",
        KeyvalueType.Vec4 => "four numbers",
        KeyvalueType.NodeRef => "the id of a node",
        KeyvalueType.TargetName => "a name",
        _ => "text",
    };
}
