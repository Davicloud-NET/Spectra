using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using System;
using System.Collections.Generic;

namespace Spectra.Kitchen.Cooking;

/// <summary>What <see cref="LooseSoundCook"/> made of one sound.</summary>
/// <param name="Cooked">The <c>.saudio</c> bytes, or null when the cook refused the sound.</param>
/// <param name="Diagnostics">What the cook said, each at the severity the gate gives it.</param>
/// <param name="Dependencies">The files the cook read or looked for, misses included.</param>
/// <param name="Key">
/// The cook's cache key for this run. <see cref="LooseSoundCook.CurrentKey"/>
/// gives the same value for as long as those files stay as they were.
/// </param>
/// <param name="IsRepeatable">
/// Whether the same files would give this result again. False when the cook
/// stopped on something else, such as a file another program holds open.
/// </param>
public sealed record LooseSoundResult(
    byte[]? Cooked,
    IReadOnlyList<CookDiagnostic> Diagnostics,
    IReadOnlyList<RuleDependency> Dependencies,
    UInt128 Key,
    bool IsRepeatable);
