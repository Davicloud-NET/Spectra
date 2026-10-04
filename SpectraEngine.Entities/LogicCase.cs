using SpectraEngine.Core.Entities;
using System;

namespace SpectraEngine.Entities;

/// <summary>
/// Compares an incoming value against a list of cases and fires the output of
/// the one it equals, or a default when it equals none.
/// </summary>
// Two numbers match as numbers, so "3", "3.0" and a counter's "3" are one case.
// Anything else matches as text, ordinally. The lowest matching case wins, and
// an empty case is unused and never matches.
// Sixteen cases, written out because the generator binds one member to one
// name. For more, wire OnDefault to a second logic_case.
[SpectraEntity("logic_case", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class LogicCase : Entity
{
    /// <summary>How many cases one entity holds.</summary>
    public const int CaseCount = 16;

    /// <summary>Fired when the value equals <see cref="Case01"/>.</summary>
    [EntityOutput]
    public const string OnCase01 = nameof(OnCase01);

    /// <summary>Fired when the value equals <see cref="Case02"/>.</summary>
    [EntityOutput]
    public const string OnCase02 = nameof(OnCase02);

    /// <summary>Fired when the value equals <see cref="Case03"/>.</summary>
    [EntityOutput]
    public const string OnCase03 = nameof(OnCase03);

    /// <summary>Fired when the value equals <see cref="Case04"/>.</summary>
    [EntityOutput]
    public const string OnCase04 = nameof(OnCase04);

    /// <summary>Fired when the value equals <see cref="Case05"/>.</summary>
    [EntityOutput]
    public const string OnCase05 = nameof(OnCase05);

    /// <summary>Fired when the value equals <see cref="Case06"/>.</summary>
    [EntityOutput]
    public const string OnCase06 = nameof(OnCase06);

    /// <summary>Fired when the value equals <see cref="Case07"/>.</summary>
    [EntityOutput]
    public const string OnCase07 = nameof(OnCase07);

    /// <summary>Fired when the value equals <see cref="Case08"/>.</summary>
    [EntityOutput]
    public const string OnCase08 = nameof(OnCase08);

    /// <summary>Fired when the value equals <see cref="Case09"/>.</summary>
    [EntityOutput]
    public const string OnCase09 = nameof(OnCase09);

    /// <summary>Fired when the value equals <see cref="Case10"/>.</summary>
    [EntityOutput]
    public const string OnCase10 = nameof(OnCase10);

    /// <summary>Fired when the value equals <see cref="Case11"/>.</summary>
    [EntityOutput]
    public const string OnCase11 = nameof(OnCase11);

    /// <summary>Fired when the value equals <see cref="Case12"/>.</summary>
    [EntityOutput]
    public const string OnCase12 = nameof(OnCase12);

    /// <summary>Fired when the value equals <see cref="Case13"/>.</summary>
    [EntityOutput]
    public const string OnCase13 = nameof(OnCase13);

    /// <summary>Fired when the value equals <see cref="Case14"/>.</summary>
    [EntityOutput]
    public const string OnCase14 = nameof(OnCase14);

    /// <summary>Fired when the value equals <see cref="Case15"/>.</summary>
    [EntityOutput]
    public const string OnCase15 = nameof(OnCase15);

    /// <summary>Fired when the value equals <see cref="Case16"/>.</summary>
    [EntityOutput]
    public const string OnCase16 = nameof(OnCase16);

    /// <summary>Fired with the value when it equals no case.</summary>
    // Carries the value, so a second logic_case wired to it can take over. The
    // numbered outputs carry nothing: a wire on them sends its own parameter.
    [EntityOutput]
    public const string OnDefault = nameof(OnDefault);

    private static readonly string[] CaseOutputs =
    [
        OnCase01, OnCase02, OnCase03, OnCase04, OnCase05, OnCase06, OnCase07, OnCase08,
        OnCase09, OnCase10, OnCase11, OnCase12, OnCase13, OnCase14, OnCase15, OnCase16,
    ];

    private readonly string[] _cases = new string[CaseCount];

    /// <summary>Creates a logic_case with every case unused.</summary>
    public LogicCase() => Array.Fill(_cases, "");

    /// <summary>The value that fires <see cref="OnCase01"/>. Empty means unused.</summary>
    [Keyvalue("case01", Display = "Case 01", Tooltip = "The value that fires OnCase01. Empty means unused.")]
    public string Case01 { get => _cases[0]; set => _cases[0] = value; }

    /// <summary>The value that fires <see cref="OnCase02"/>. Empty means unused.</summary>
    [Keyvalue("case02", Display = "Case 02", Tooltip = "The value that fires OnCase02. Empty means unused.")]
    public string Case02 { get => _cases[1]; set => _cases[1] = value; }

    /// <summary>The value that fires <see cref="OnCase03"/>. Empty means unused.</summary>
    [Keyvalue("case03", Display = "Case 03", Tooltip = "The value that fires OnCase03. Empty means unused.")]
    public string Case03 { get => _cases[2]; set => _cases[2] = value; }

    /// <summary>The value that fires <see cref="OnCase04"/>. Empty means unused.</summary>
    [Keyvalue("case04", Display = "Case 04", Tooltip = "The value that fires OnCase04. Empty means unused.")]
    public string Case04 { get => _cases[3]; set => _cases[3] = value; }

    /// <summary>The value that fires <see cref="OnCase05"/>. Empty means unused.</summary>
    [Keyvalue("case05", Display = "Case 05", Tooltip = "The value that fires OnCase05. Empty means unused.")]
    public string Case05 { get => _cases[4]; set => _cases[4] = value; }

    /// <summary>The value that fires <see cref="OnCase06"/>. Empty means unused.</summary>
    [Keyvalue("case06", Display = "Case 06", Tooltip = "The value that fires OnCase06. Empty means unused.")]
    public string Case06 { get => _cases[5]; set => _cases[5] = value; }

    /// <summary>The value that fires <see cref="OnCase07"/>. Empty means unused.</summary>
    [Keyvalue("case07", Display = "Case 07", Tooltip = "The value that fires OnCase07. Empty means unused.")]
    public string Case07 { get => _cases[6]; set => _cases[6] = value; }

    /// <summary>The value that fires <see cref="OnCase08"/>. Empty means unused.</summary>
    [Keyvalue("case08", Display = "Case 08", Tooltip = "The value that fires OnCase08. Empty means unused.")]
    public string Case08 { get => _cases[7]; set => _cases[7] = value; }

    /// <summary>The value that fires <see cref="OnCase09"/>. Empty means unused.</summary>
    [Keyvalue("case09", Display = "Case 09", Tooltip = "The value that fires OnCase09. Empty means unused.")]
    public string Case09 { get => _cases[8]; set => _cases[8] = value; }

    /// <summary>The value that fires <see cref="OnCase10"/>. Empty means unused.</summary>
    [Keyvalue("case10", Display = "Case 10", Tooltip = "The value that fires OnCase10. Empty means unused.")]
    public string Case10 { get => _cases[9]; set => _cases[9] = value; }

    /// <summary>The value that fires <see cref="OnCase11"/>. Empty means unused.</summary>
    [Keyvalue("case11", Display = "Case 11", Tooltip = "The value that fires OnCase11. Empty means unused.")]
    public string Case11 { get => _cases[10]; set => _cases[10] = value; }

    /// <summary>The value that fires <see cref="OnCase12"/>. Empty means unused.</summary>
    [Keyvalue("case12", Display = "Case 12", Tooltip = "The value that fires OnCase12. Empty means unused.")]
    public string Case12 { get => _cases[11]; set => _cases[11] = value; }

    /// <summary>The value that fires <see cref="OnCase13"/>. Empty means unused.</summary>
    [Keyvalue("case13", Display = "Case 13", Tooltip = "The value that fires OnCase13. Empty means unused.")]
    public string Case13 { get => _cases[12]; set => _cases[12] = value; }

    /// <summary>The value that fires <see cref="OnCase14"/>. Empty means unused.</summary>
    [Keyvalue("case14", Display = "Case 14", Tooltip = "The value that fires OnCase14. Empty means unused.")]
    public string Case14 { get => _cases[13]; set => _cases[13] = value; }

    /// <summary>The value that fires <see cref="OnCase15"/>. Empty means unused.</summary>
    [Keyvalue("case15", Display = "Case 15", Tooltip = "The value that fires OnCase15. Empty means unused.")]
    public string Case15 { get => _cases[14]; set => _cases[14] = value; }

    /// <summary>The value that fires <see cref="OnCase16"/>. Empty means unused.</summary>
    [Keyvalue("case16", Display = "Case 16", Tooltip = "The value that fires OnCase16. Empty means unused.")]
    public string Case16 { get => _cases[15]; set => _cases[15] = value; }

    // No PickRandom: the runtime has no deterministic random source to draw from.
    [EntityInput("InValue")]
    private void InValue(ref EntityInputContext context)
    {
        int match = IndexOfCase(context.Parameter);
        if (match < 0)
        {
            FireOnDefault(context.Activator, context.Parameter);
            return;
        }

        // By name: the generated FireOnCase methods cannot be indexed.
        FireOutput(CaseOutputs[match], context.Activator);
    }

    private int IndexOfCase(string value)
    {
        bool isNumber = KeyvalueWire.TryParseFloat(value, out float number);

        for (int i = 0; i < _cases.Length; i++)
        {
            string candidate = _cases[i];
            if (candidate.Length == 0)
                continue;

            bool matches = isNumber && KeyvalueWire.TryParseFloat(candidate, out float caseNumber)
                ? number == caseNumber
                : string.Equals(candidate, value, StringComparison.Ordinal);

            if (matches)
                return i;
        }

        return -1;
    }
}
