using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>What a problem belongs to, and therefore when it stops applying.</summary>
public enum ProblemScope
{
    /// <summary>The session. Cleared when the project closes.</summary>
    Session,

    /// <summary>The open level. Cleared when another one is opened.</summary>
    Map,

    /// <summary>The last cooked-content validation. Cleared when it runs again.</summary>
    Cook,
}

/// <summary>One standing condition, however many times it has been reported.</summary>
public sealed class ProblemEntry : ObservableObject
{
    private string _message;
    private int _count;
    private DateTime _lastSeen;

    internal ProblemEntry(
        OutputSeverity severity,
        ProblemScope scope,
        string template,
        string message,
        string subject,
        Guid nodeId,
        DateTime firstSeen)
    {
        Severity = severity;
        Scope = scope;
        Template = template;
        Subject = subject;
        NodeId = nodeId;
        FirstSeen = firstSeen;
        _message = message;
        _lastSeen = firstSeen;
        _count = 1;
    }

    /// <summary>How loud it is. Warning or Error; nothing quieter is a problem.</summary>
    public OutputSeverity Severity { get; }

    /// <summary>What clears it.</summary>
    public ProblemScope Scope { get; }

    /// <summary>The message template, which is half of this row's identity.</summary>
    public string Template { get; }

    /// <summary>The asset path or file it is about, or empty.</summary>
    public string Subject { get; }

    /// <summary>The node it is about, or <see cref="Guid.Empty"/>.</summary>
    public Guid NodeId { get; }

    /// <summary>When it was first reported.</summary>
    public DateTime FirstSeen { get; }

    /// <summary>The most recent rendering of it.</summary>
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>How many times it has been reported.</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (Set(ref _count, value)) Raise(nameof(CountLabel));
        }
    }

    /// <summary>When it was last reported.</summary>
    public DateTime LastSeen
    {
        get => _lastSeen;
        private set
        {
            if (Set(ref _lastSeen, value)) Raise(nameof(TimeLabel));
        }
    }

    /// <summary>"x12" once it has happened more than once, else empty.</summary>
    public string CountLabel =>
        _count > 1 ? "x" + _count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>When it was last reported, as <c>HH:mm:ss</c>.</summary>
    public string TimeLabel => _lastSeen.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Whether this row should carry the error colour.</summary>
    public bool IsError => Severity == OutputSeverity.Error;

    /// <summary>Whether this row should carry the warning colour.</summary>
    public bool IsWarning => Severity == OutputSeverity.Warning;

    /// <summary>Whether there is a subject worth showing.</summary>
    public bool HasSubject => Subject.Length > 0;

    /// <summary>Whether activating this row can select something.</summary>
    public bool HasNode => NodeId != Guid.Empty;

    /// <summary>The colour this row is set in, from the token dictionary.</summary>
    public IBrush? SeverityBrush => Resource(IsError ? "SpectraTextDanger" : "SpectraMode");

    internal void Repeat(string message, DateTime seen)
    {
        Message = message;
        LastSeen = seen;
        Count++;
    }

    // The same standing condition in newer words. Not a repeat, so no count.
    internal void Restate(string message) => Message = message;

    private static IBrush? Resource(string key)
        => Application.Current?.TryFindResource(key, out object? value) == true ? value as IBrush : null;
}

/// <summary>
/// The standing problems: one row per (severity, template, subject), repeats
/// counted. A row stays until it is resolved, dismissed or its scope is cleared.
/// UI thread only.
/// </summary>
// Keyed on the template, not the rendered message, so one condition about two
// files is two rows. Rows never expire on a timer.
public sealed class ProblemList : ObservableObject
{
    private readonly Dictionary<(OutputSeverity Severity, string Template, string Subject), ProblemEntry> _index =
        new();

    private int _errorCount;
    private int _warningCount;

    /// <summary>The problems, in the order they were first seen.</summary>
    public ObservableCollection<ProblemEntry> Entries { get; } = [];

    /// <summary>How many distinct errors stand.</summary>
    public int ErrorCount
    {
        get => _errorCount;
        private set
        {
            if (Set(ref _errorCount, value)) RaiseSummaries();
        }
    }

    /// <summary>How many distinct warnings stand.</summary>
    public int WarningCount
    {
        get => _warningCount;
        private set
        {
            if (Set(ref _warningCount, value)) RaiseSummaries();
        }
    }

    /// <summary>Whether anything at all is wrong.</summary>
    public bool HasProblems => Entries.Count > 0;

    /// <summary>A one-line verdict for the panel header.</summary>
    public string Summary => (_errorCount, _warningCount) switch
    {
        (0, 0) => "no problems",
        (0, 1) => "1 warning",
        (0, var w) => $"{w} warnings",
        (1, 0) => "1 error",
        (var e, 0) => $"{e} errors",
        (1, 1) => "1 error, 1 warning",
        (var e, 1) => $"{e} errors, 1 warning",
        (1, var w) => $"1 error, {w} warnings",
        var (e, w) => $"{e} errors, {w} warnings",
    };

    /// <summary>A count for the status bar, which has no room for the verdict.</summary>
    public string CountLabel => Entries.Count == 1 ? "1 problem" : $"{Entries.Count} problems";

    /// <summary>
    /// Records a condition, or counts a repeat of one already standing.
    /// Only warnings and errors are recorded; anything else returns null.
    /// </summary>
    public ProblemEntry? Report(
        OutputSeverity severity,
        string template,
        string message,
        string subject = "",
        ProblemScope scope = ProblemScope.Session,
        Guid nodeId = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(message);

        if (severity is not (OutputSeverity.Warning or OutputSeverity.Error))
            return null;

        subject ??= string.Empty;
        var key = (severity, template, subject);
        DateTime now = DateTime.Now;

        if (_index.TryGetValue(key, out ProblemEntry? existing))
        {
            existing.Repeat(message, now);
            return existing;
        }

        var entry = new ProblemEntry(severity, scope, template, message, subject, nodeId, now);
        _index[key] = entry;
        Entries.Add(entry);

        if (severity == OutputSeverity.Error) ErrorCount++;
        else WarningCount++;

        RaiseCounts();
        return entry;
    }

    /// <summary>
    /// Makes one template's rows match a freshly computed list, for a
    /// condition that is found by looking at the scene and never logged. A row
    /// no longer listed goes, a new one is added, and one still listed keeps
    /// its place and its first-seen time.
    /// </summary>
    /// <returns>How many rows were added or removed.</returns>
    public int Replace(
        OutputSeverity severity, string template, ProblemScope scope, IReadOnlyList<ProblemRow> current)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(current);

        // The first row wins a subject, as it would through Report.
        var wanted = new Dictionary<string, ProblemRow>(StringComparer.Ordinal);
        foreach (ProblemRow row in current)
            wanted.TryAdd(row.Subject, row);

        int changed = 0;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            ProblemEntry entry = Entries[i];
            if (entry.Severity != severity || !string.Equals(entry.Template, template, StringComparison.Ordinal))
                continue;

            // The same subject on another node is another problem: the row
            // selects its node.
            if (wanted.TryGetValue(entry.Subject, out ProblemRow standing) && standing.NodeId == entry.NodeId)
            {
                entry.Restate(standing.Message);
                wanted.Remove(entry.Subject);
                continue;
            }

            RemoveAt(i);
            changed++;
        }

        foreach (ProblemRow row in current)
        {
            if (!wanted.Remove(row.Subject, out ProblemRow added))
                continue;

            if (Report(severity, template, added.Message, added.Subject, scope, added.NodeId) is not null)
                changed++;
        }

        if (changed > 0) RaiseCounts();
        return changed;
    }

    /// <summary>Drops every problem about <paramref name="subject"/>. Returns how many rows went.</summary>
    public int Resolve(string subject)
    {
        if (string.IsNullOrEmpty(subject)) return 0;

        int removed = 0;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(Entries[i].Subject, subject, StringComparison.OrdinalIgnoreCase))
                continue;

            RemoveAt(i);
            removed++;
        }

        if (removed > 0) RaiseCounts();
        return removed;
    }

    /// <summary>Drops one problem, because somebody dismissed it.</summary>
    public void Remove(ProblemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        int index = Entries.IndexOf(entry);
        if (index < 0) return;

        RemoveAt(index);
        RaiseCounts();
    }

    /// <summary>Drops every problem in one scope. Returns how many rows went.</summary>
    public int ClearScope(ProblemScope scope)
    {
        int removed = 0;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i].Scope != scope) continue;

            RemoveAt(i);
            removed++;
        }

        if (removed > 0) RaiseCounts();
        return removed;
    }

    /// <summary>Empties the list.</summary>
    public void Clear()
    {
        if (Entries.Count == 0) return;

        Entries.Clear();
        _index.Clear();
        ErrorCount = 0;
        WarningCount = 0;
        RaiseCounts();
    }

    private void RemoveAt(int index)
    {
        ProblemEntry entry = Entries[index];
        Entries.RemoveAt(index);
        _index.Remove((entry.Severity, entry.Template, entry.Subject));

        if (entry.Severity == OutputSeverity.Error) ErrorCount--;
        else WarningCount--;
    }

    private void RaiseCounts()
    {
        Raise(nameof(HasProblems));
        Raise(nameof(CountLabel));
    }

    private void RaiseSummaries() => Raise(nameof(Summary));
}
