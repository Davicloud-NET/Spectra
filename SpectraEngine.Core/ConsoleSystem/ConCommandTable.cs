using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>The commands one console knows, found by name in any case.</summary>
public sealed class ConCommandTable
{
    private readonly Dictionary<string, ConCommand> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConCommand> _ordered = [];

    /// <summary>Every command, in ordinal name order.</summary>
    public IReadOnlyList<ConCommand> Commands => _ordered;

    /// <summary>Adds a command.</summary>
    /// <exception cref="InvalidOperationException">The table already has a command by that name.</exception>
    public void Add(ConCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_byName.TryGetValue(command.Name, out ConCommand? existing))
        {
            throw new InvalidOperationException(
                $"A console command named '{existing.Name}' is already registered.");
        }

        _byName.Add(command.Name, command);

        int index = 0;
        while (index < _ordered.Count && string.CompareOrdinal(_ordered[index].Name, command.Name) < 0)
            index++;
        _ordered.Insert(index, command);
    }

    /// <summary>Finds a command by name, whatever case it is typed in.</summary>
    public bool TryGet(ReadOnlySpan<char> name, [NotNullWhen(true)] out ConCommand? command) =>
        _byName.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out command);
}
