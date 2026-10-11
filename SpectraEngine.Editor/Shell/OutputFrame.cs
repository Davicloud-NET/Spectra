using SpectraEngine.Core.ConsoleSystem;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>One call under an error in the output: where it was, and a way to go there.</summary>
public sealed class OutputFrame
{
    private readonly Action<ConsolePlace> _open;

    internal OutputFrame(ConsolePlace place, Action<ConsolePlace> open)
    {
        Place = place;
        _open = open;
        Label = place.Function is null
            ? OutputPlaceText.Short(place)
            : $"{OutputPlaceText.Short(place)} in {place.Function}";
    }

    /// <summary>Where the call was.</summary>
    public ConsolePlace Place { get; }

    /// <summary>The file's name and the line, and the function when it has a name.</summary>
    public string Label { get; }

    /// <summary>What a click on the row does, with the whole path.</summary>
    public string Tip => OutputPlaceText.Tip(Place);

    /// <summary>Asks for the place to be shown.</summary>
    public void Open() => _open(Place);
}
