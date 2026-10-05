using Spectra.Kitchen.Diagnostics;

namespace SpectraEngine.Editor.Sounds;

// One thing the cook said about a sound. Kept with the cooked bytes, so a
// later start of the editor can say it again without cooking.
internal readonly record struct SoundCookNote(CookDiagnosticSeverity Severity, string Text)
{
    public static SoundCookNote From(CookDiagnostic diagnostic) =>
        new(diagnostic.Severity, $"{diagnostic.Id}: {diagnostic.Message}");
}
