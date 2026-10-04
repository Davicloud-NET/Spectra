using System.Collections.Generic;

namespace Spectra.Kitchen.Diagnostics;

// A run's diagnostics in order. Add is the only way in and applies CookGate,
// so no reporting site can skip the gate.
internal sealed class CookDiagnosticLog
{
    private readonly List<CookDiagnostic> _entries = [];
    private readonly bool _strict;

    public CookDiagnosticLog(bool strict) => _strict = strict;

    public IReadOnlyList<CookDiagnostic> Entries => _entries;

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public bool Failed => ErrorCount > 0;

    public void Add(CookDiagnostic diagnostic)
    {
        CookDiagnostic decided = CookGate.Apply(diagnostic, _strict);
        _entries.Add(decided);

        if (decided.IsError) ErrorCount++;
        else if (decided.Severity == CookDiagnosticSeverity.Warning) WarningCount++;
    }

    public void AddRange(IReadOnlyList<CookDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++) Add(diagnostics[i]);
    }
}
