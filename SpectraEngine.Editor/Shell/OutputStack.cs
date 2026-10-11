using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The calls under an error in the output, innermost first. The first few
/// show, and the rest when somebody asks for them.
/// </summary>
public sealed class OutputStack : ObservableObject
{
    /// <summary>How many calls show before the rest is asked for.</summary>
    public const int ShownAtFirst = 3;

    private readonly OutputFrame[] _frames;
    private readonly OutputFrame[] _first;
    private bool _isOpen;

    internal OutputStack(IReadOnlyList<ConsolePlace> stack, Action<ConsolePlace> open)
    {
        _frames = new OutputFrame[stack.Count];
        for (int i = 0; i < _frames.Length; i++)
            _frames[i] = new OutputFrame(stack[i], open);

        // One hidden call would cost the row that says so. It shows.
        _first = _frames.Length > ShownAtFirst + 1 ? _frames[..ShownAtFirst] : _frames;
    }

    /// <summary>The calls that show now.</summary>
    public IReadOnlyList<OutputFrame> Shown => _isOpen ? _frames : _first;

    /// <summary>Whether some calls are held back, so there is something to ask for.</summary>
    public bool CanFold => _first.Length < _frames.Length;

    /// <summary>What the row under the calls says: how many more there are, or how to hide them.</summary>
    public string FoldLabel => _isOpen
        ? "Show fewer calls"
        : $"Show {(_frames.Length - _first.Length).ToString(CultureInfo.InvariantCulture)} more calls";

    /// <summary>Shows every call, or the first few again.</summary>
    public void Toggle()
    {
        if (!CanFold)
            return;

        _isOpen = !_isOpen;
        Raise(nameof(Shown));
        Raise(nameof(FoldLabel));
    }

    internal bool IsSame(IReadOnlyList<ConsolePlace> stack)
    {
        if (stack.Count != _frames.Length)
            return false;

        for (int i = 0; i < _frames.Length; i++)
        {
            if (_frames[i].Place != stack[i])
                return false;
        }

        return true;
    }
}
