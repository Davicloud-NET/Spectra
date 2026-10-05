using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Serialization;
using SpectraEngine.Editor.Viewport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpectraEngine.Editor.Shell;

/// <summary>One project the shell has opened before.</summary>
public sealed record RecentProject(string Path, string Name, DateTime OpenedUtc);

/// <summary>
/// The shell's per-user state: recent projects, viewport choice, layout.
/// Stored under the user profile. UI thread only.
/// </summary>
// Hand-written JSON codec: a reflection serializer does not survive trimming.
// Unknown members are skipped, not preserved. A missing or corrupt file loads
// as defaults and is rewritten whole on the next save.
public sealed class EditorSettings
{
    private const int MaxRecentProjects = 10;

    private readonly List<RecentProject> _recentProjects = [];

    /// <summary>Most recently opened first.</summary>
    public IReadOnlyList<RecentProject> RecentProjects => _recentProjects;

    /// <summary>Where the settings live for this user.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spectra", "editor.json");

    /// <summary>
    /// Records that a project was opened or created, moving it to the front.
    /// </summary>
    public void TouchProject(string path, string name, DateTime openedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string full = Path.GetFullPath(path);
        _recentProjects.RemoveAll(p =>
            string.Equals(p.Path, full, StringComparison.OrdinalIgnoreCase));

        _recentProjects.Insert(0, new RecentProject(full, name, openedUtc));
        _forgotten.Remove(full);
        if (_recentProjects.Count > MaxRecentProjects)
            _recentProjects.RemoveRange(MaxRecentProjects, _recentProjects.Count - MaxRecentProjects);
    }

    /// <summary>Drops one entry, for a card whose folder no longer exists.</summary>
    public void ForgetProject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        _recentProjects.RemoveAll(p =>
            string.Equals(p.Path, full, StringComparison.OrdinalIgnoreCase));

        // So the save-time merge cannot bring it back from another shell's copy.
        _forgotten.Add(full);
    }

    private readonly HashSet<string> _forgotten = new(StringComparer.OrdinalIgnoreCase);

    private ViewportPreference _viewport = ViewportPreference.Default;
    private DateTime _viewportRecordedUtc = DateTime.MinValue;

    /// <summary>
    /// The requested viewport mode and this machine's composited history.
    /// See <see cref="ViewportModePolicy"/>.
    /// </summary>
    public ViewportPreference ViewportPreference => _viewport;

    /// <summary>Records which viewport to ask for from now on. Persists across launches.</summary>
    public void SetViewportMode(ViewportMode mode)
    {
        if (_viewport.Mode == mode)
            return;

        _viewport = _viewport with { Mode = mode };
        _viewportRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-anchors the composited history on this machine's adapter and driver.
    /// Call only when they were measured; empty strings would wipe the history.
    /// </summary>
    public void RebaseViewport(string adapterLuid, string driverVersion)
    {
        ViewportPreference rebased = ViewportModePolicy.Rebase(_viewport, adapterLuid, driverVersion);
        if (rebased == _viewport)
            return;

        _viewport = rebased;
        _viewportRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Folds one finished composited session into the history: one longer if it
    /// was green, back to zero if not. Never call for a native session.
    /// </summary>
    public void RecordCompositedSession(bool sessionGreen)
    {
        _viewport = ViewportModePolicy.Record(_viewport, sessionGreen);
        _viewportRecordedUtc = DateTime.UtcNow;
    }

    private bool _ribbonExpanded = true;
    private DateTime _ribbonRecordedUtc = DateTime.MinValue;

    /// <summary>Whether the command ribbon is pinned open.</summary>
    // The active tab is not stored: every launch opens on the default tab, so
    // Insert is never hidden behind a click.
    public bool RibbonExpanded => _ribbonExpanded;

    private ContentViewMode _contentView = ContentViewMode.Grid;
    private DateTime _contentRecordedUtc = DateTime.MinValue;

    /// <summary>Whether the content browser draws tiles or rows.</summary>
    public ContentViewMode ContentView => _contentView;

    private WorkspacePreset _workspacePreset = WorkspacePreset.Compact;
    private double _drawerHeight = WorkspaceLayout.DefaultDrawerHeight;
    private DateTime _workspaceRecordedUtc = DateTime.MinValue;

    /// <summary>How the window is arranged. Compact by default.</summary>
    public WorkspacePreset WorkspacePreset => _workspacePreset;

    /// <summary>How tall the bottom drawer opens.</summary>
    public double DrawerHeight => _drawerHeight;

    /// <summary>Records the arrangement for next time.</summary>
    public void SetWorkspacePreset(WorkspacePreset preset)
    {
        if (_workspacePreset == preset) return;

        _workspacePreset = preset;
        _workspaceRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>Records the drawer height after a drag.</summary>
    public void SetDrawerHeight(double height)
    {
        if (!double.IsFinite(height) || height < 0 || _drawerHeight == height) return;

        _drawerHeight = height;
        _workspaceRecordedUtc = DateTime.UtcNow;
    }

    private ViewArrangement _viewArrangement = ViewArrangement.Single;
    private ViewArrangement _lastViewSplit = ViewPaneLayout.DefaultSplit;
    private double _viewColumnSplit = ViewPaneLayout.DefaultColumnSplit;
    private double _viewRowSplit = ViewPaneLayout.DefaultRowSplit;
    private DateTime _viewsRecordedUtc = DateTime.MinValue;

    /// <summary>Which view panes show. The 3D view alone by default.</summary>
    public ViewArrangement ViewArrangement => _viewArrangement;

    /// <summary>Where the Logic view last showed, for the key that brings it back.</summary>
    public ViewArrangement LastViewSplit => _lastViewSplit;

    /// <summary>The 3D view's share of the width beside the Logic view.</summary>
    public double ViewColumnSplit => _viewColumnSplit;

    /// <summary>The 3D view's share of the height above the Logic view.</summary>
    public double ViewRowSplit => _viewRowSplit;

    /// <summary>Records the arrangement for next time, and a split as the last one used.</summary>
    public void SetViewArrangement(ViewArrangement arrangement)
    {
        if (_viewArrangement == arrangement) return;

        _viewArrangement = arrangement;
        if (arrangement != ViewArrangement.Single) _lastViewSplit = arrangement;
        _viewsRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>Records the column split after a drag.</summary>
    public void SetViewColumnSplit(double split)
    {
        if (!ViewPaneLayout.IsSplit(split) || _viewColumnSplit == split) return;

        _viewColumnSplit = split;
        _viewsRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>Records the row split after a drag.</summary>
    public void SetViewRowSplit(double split)
    {
        if (!ViewPaneLayout.IsSplit(split) || _viewRowSplit == split) return;

        _viewRowSplit = split;
        _viewsRecordedUtc = DateTime.UtcNow;
    }

    private bool _diagnosticsReadouts;
    private DateTime _diagnosticsRecordedUtc = DateTime.MinValue;

    /// <summary>Whether the status bar shows the engine counters.</summary>
    public bool DiagnosticsReadouts => _diagnosticsReadouts;

    /// <summary>Records the choice for next time.</summary>
    public void SetDiagnosticsReadouts(bool shown)
    {
        if (_diagnosticsReadouts == shown) return;

        _diagnosticsReadouts = shown;
        _diagnosticsRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>Records how the content browser is being viewed.</summary>
    public void SetContentView(ContentViewMode mode)
    {
        if (_contentView == mode)
            return;

        _contentView = mode;
        _contentRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>Records the pin state for next time.</summary>
    public void SetRibbonExpanded(bool expanded)
    {
        if (_ribbonExpanded == expanded)
            return;

        _ribbonExpanded = expanded;
        _ribbonRecordedUtc = DateTime.UtcNow;
    }

    /// <summary>Loads the settings, or returns empty ones when there is nothing to load.</summary>
    public static EditorSettings Load(ILogger logger) => Load(DefaultPath, logger);

    /// <summary>Loads from an explicit path, for tests.</summary>
    public static EditorSettings Load(string path, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var settings = new EditorSettings();
        if (!File.Exists(path))
            return settings;

        try
        {
            settings.Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (IsSettingsReadFailure(ex))
        {
            logger.LogWarning(ex, "Could not read editor settings at {Path}; starting fresh", path);
            settings._recentProjects.Clear();

            // Reset everything: a half-read viewport block is a green count
            // with no adapter behind it.
            settings._viewport = ViewportPreference.Default;
            settings._viewportRecordedUtc = DateTime.MinValue;

            settings._ribbonExpanded = true;
            settings._ribbonRecordedUtc = DateTime.MinValue;
            settings._workspacePreset = WorkspacePreset.Compact;
            settings._drawerHeight = WorkspaceLayout.DefaultDrawerHeight;
            settings._workspaceRecordedUtc = DateTime.MinValue;
            settings._viewArrangement = ViewArrangement.Single;
            settings._lastViewSplit = ViewPaneLayout.DefaultSplit;
            settings._viewColumnSplit = ViewPaneLayout.DefaultColumnSplit;
            settings._viewRowSplit = ViewPaneLayout.DefaultRowSplit;
            settings._viewsRecordedUtc = DateTime.MinValue;
            settings._diagnosticsReadouts = false;
            settings._diagnosticsRecordedUtc = DateTime.MinValue;
        }

        return settings;
    }

    // InvalidOperationException and FormatException are what the reader throws
    // for a member of the wrong type ("path": 5).
    private static bool IsSettingsReadFailure(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException
            or InvalidOperationException or FormatException;

    /// <summary>Writes the settings. Failures are logged, not thrown.</summary>
    public void Save(ILogger logger) => Save(DefaultPath, logger);

    /// <summary>Saves to an explicit path, for tests.</summary>
    public void Save(string path, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);

            MergeFromDisk(path);
            File.WriteAllBytes(path, CanonicalJson.Write(Write));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write editor settings to {Path}", path);
        }
    }

    // Folds in what another shell wrote since this one loaded, so two editors
    // do not erase each other's history. An unreadable file merges nothing.
    private void MergeFromDisk(string path)
    {
        if (!File.Exists(path))
            return;

        var onDisk = new EditorSettings();
        try
        {
            onDisk.Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (IsSettingsReadFailure(ex))
        {
            return;
        }

        foreach (RecentProject theirs in onDisk._recentProjects)
        {
            if (_forgotten.Contains(theirs.Path))
                continue;

            int mine = _recentProjects.FindIndex(p =>
                string.Equals(p.Path, theirs.Path, StringComparison.OrdinalIgnoreCase));

            if (mine < 0)
                _recentProjects.Add(theirs);
            else if (theirs.OpenedUtc > _recentProjects[mine].OpenedUtc)
                _recentProjects[mine] = theirs;
        }

        _recentProjects.Sort((a, b) => b.OpenedUtc.CompareTo(a.OpenedUtc));
        if (_recentProjects.Count > MaxRecentProjects)
            _recentProjects.RemoveRange(MaxRecentProjects, _recentProjects.Count - MaxRecentProjects);

        // Each block below is one state, so the most recent writer wins whole.
        // Merging fields would mix two shells' green counts.
        if (onDisk._viewportRecordedUtc > _viewportRecordedUtc)
        {
            _viewport = onDisk._viewport;
            _viewportRecordedUtc = onDisk._viewportRecordedUtc;
        }

        if (onDisk._ribbonRecordedUtc > _ribbonRecordedUtc)
        {
            _ribbonExpanded = onDisk._ribbonExpanded;
            _ribbonRecordedUtc = onDisk._ribbonRecordedUtc;
        }

        if (onDisk._workspaceRecordedUtc > _workspaceRecordedUtc)
        {
            _workspacePreset = onDisk._workspacePreset;
            _drawerHeight = onDisk._drawerHeight;
            _workspaceRecordedUtc = onDisk._workspaceRecordedUtc;
        }

        if (onDisk._viewsRecordedUtc > _viewsRecordedUtc)
        {
            _viewArrangement = onDisk._viewArrangement;
            _lastViewSplit = onDisk._lastViewSplit;
            _viewColumnSplit = onDisk._viewColumnSplit;
            _viewRowSplit = onDisk._viewRowSplit;
            _viewsRecordedUtc = onDisk._viewsRecordedUtc;
        }

        if (onDisk._diagnosticsRecordedUtc > _diagnosticsRecordedUtc)
        {
            _diagnosticsReadouts = onDisk._diagnosticsReadouts;
            _diagnosticsRecordedUtc = onDisk._diagnosticsRecordedUtc;
        }

        if (onDisk._contentRecordedUtc > _contentRecordedUtc)
        {
            _contentView = onDisk._contentView;
            _contentRecordedUtc = onDisk._contentRecordedUtc;
        }
    }

    private void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        writer.WriteStartObject("viewport");
        writer.WriteString("mode", ViewportModePolicy.NameOf(_viewport.Mode));
        writer.WriteNumber("greenSessions", _viewport.GreenSessions);
        writer.WriteString("adapterLuid", _viewport.AdapterLuid);
        writer.WriteString("driverVersion", _viewport.DriverVersion);
        writer.WriteString("recordedUtc", _viewportRecordedUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("ribbon");
        writer.WriteBoolean("expanded", _ribbonExpanded);
        writer.WriteString("recordedUtc", _ribbonRecordedUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("workspace");
        writer.WriteString("preset", WorkspaceLayout.NameOf(_workspacePreset));
        writer.WriteNumber("drawerHeight", _drawerHeight);
        writer.WriteString("recordedUtc", _workspaceRecordedUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("views");
        writer.WriteString("arrangement", ViewPaneLayout.NameOf(_viewArrangement));
        writer.WriteString("lastSplit", ViewPaneLayout.NameOf(_lastViewSplit));
        writer.WriteNumber("columnSplit", _viewColumnSplit);
        writer.WriteNumber("rowSplit", _viewRowSplit);
        writer.WriteString("recordedUtc", _viewsRecordedUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("content");
        writer.WriteString("viewMode", ContentViewNames.NameOf(_contentView));
        writer.WriteString("recordedUtc", _contentRecordedUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("diagnostics");
        writer.WriteBoolean("readouts", _diagnosticsReadouts);
        writer.WriteString("recordedUtc", _diagnosticsRecordedUtc.ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartArray("recentProjects");
        foreach (RecentProject project in _recentProjects)
        {
            writer.WriteStartObject();
            writer.WriteString("path", project.Path);
            writer.WriteString("name", project.Name);
            writer.WriteString("openedUtc", project.OpenedUtc.ToString("O"));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private void Read(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(CanonicalJson.StripBom(utf8), CanonicalJson.ReaderOptions);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("The settings root must be an object.");

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("recentProjects"))
            {
                ReadRecentProjects(ref reader);
            }
            else if (reader.ValueTextEquals("viewport"))
            {
                ReadViewport(ref reader);
            }
            else if (reader.ValueTextEquals("workspace"))
            {
                ReadWorkspace(ref reader);
            }
            else if (reader.ValueTextEquals("views"))
            {
                ReadViews(ref reader);
            }
            else if (reader.ValueTextEquals("content"))
            {
                ReadContent(ref reader);
            }
            else if (reader.ValueTextEquals("diagnostics"))
            {
                ReadDiagnostics(ref reader);
            }
            else if (reader.ValueTextEquals("ribbon"))
            {
                ReadRibbon(ref reader);
            }
            else
            {
                // Unknown member from a newer shell.
                reader.Read();
                reader.Skip();
            }
        }
    }

    // An unknown mode word falls back to auto rather than failing the file.
    private void ReadViewport(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("'viewport' must be an object.");

        ViewportMode mode = ViewportPreference.Default.Mode;
        int green = 0;
        string luid = string.Empty;
        string driver = string.Empty;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("mode"))
            {
                reader.Read();
                ViewportModePolicy.TryParseMode(reader.GetString(), out mode);
            }
            else if (reader.ValueTextEquals("greenSessions"))
            {
                reader.Read();
                green = Math.Max(0, reader.GetInt32());
            }
            else if (reader.ValueTextEquals("adapterLuid"))
            {
                reader.Read();
                luid = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("driverVersion"))
            {
                reader.Read();
                driver = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        _viewport = new ViewportPreference(mode, green, luid, driver);
        _viewportRecordedUtc = recorded;
    }

    private void ReadRibbon(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("'ribbon' must be an object.");

        bool expanded = true;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("expanded"))
            {
                reader.Read();
                expanded = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        _ribbonExpanded = expanded;
        _ribbonRecordedUtc = recorded;
    }

    private void ReadWorkspace(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("'workspace' must be an object.");

        WorkspacePreset preset = WorkspacePreset.Compact;
        double drawer = WorkspaceLayout.DefaultDrawerHeight;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("preset"))
            {
                reader.Read();

                // An unknown word reads as compact.
                WorkspaceLayout.TryParse(reader.GetString(), out preset);
            }
            else if (reader.ValueTextEquals("drawerHeight"))
            {
                reader.Read();
                drawer = reader.GetDouble();
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        _workspacePreset = preset;
        _drawerHeight = double.IsFinite(drawer) && drawer >= 0
            ? drawer
            : WorkspaceLayout.DefaultDrawerHeight;
        _workspaceRecordedUtc = recorded;
    }

    private void ReadViews(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("'views' must be an object.");

        ViewArrangement arrangement = ViewArrangement.Single;
        ViewArrangement lastSplit = ViewPaneLayout.DefaultSplit;
        double columnSplit = ViewPaneLayout.DefaultColumnSplit;
        double rowSplit = ViewPaneLayout.DefaultRowSplit;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("arrangement"))
            {
                reader.Read();

                // An unknown word reads as single.
                ViewPaneLayout.TryParse(reader.GetString(), out arrangement);
            }
            else if (reader.ValueTextEquals("lastSplit"))
            {
                reader.Read();
                ViewPaneLayout.TryParse(reader.GetString(), out lastSplit);
            }
            else if (reader.ValueTextEquals("columnSplit"))
            {
                reader.Read();
                columnSplit = reader.GetDouble();
            }
            else if (reader.ValueTextEquals("rowSplit"))
            {
                reader.Read();
                rowSplit = reader.GetDouble();
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        _viewArrangement = arrangement;

        // A split that shows is the last one used, whatever the file says.
        // Single is no split, so a file naming it there gets the default.
        if (arrangement != ViewArrangement.Single)
            _lastViewSplit = arrangement;
        else
            _lastViewSplit = lastSplit == ViewArrangement.Single ? ViewPaneLayout.DefaultSplit : lastSplit;

        _viewColumnSplit = ViewPaneLayout.IsSplit(columnSplit) ? columnSplit : ViewPaneLayout.DefaultColumnSplit;
        _viewRowSplit = ViewPaneLayout.IsSplit(rowSplit) ? rowSplit : ViewPaneLayout.DefaultRowSplit;
        _viewsRecordedUtc = recorded;
    }

    private void ReadDiagnostics(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("'diagnostics' must be an object.");

        bool readouts = false;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("readouts"))
            {
                reader.Read();
                readouts = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        _diagnosticsReadouts = readouts;
        _diagnosticsRecordedUtc = recorded;
    }

    private void ReadContent(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("'content' must be an object.");

        ContentViewMode mode = ContentViewMode.Grid;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("viewMode"))
            {
                reader.Read();
                if (!ContentViewNames.TryParse(reader.GetString(), out mode))
                    mode = ContentViewMode.Grid;
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        _contentView = mode;
        _contentRecordedUtc = recorded;
    }

    private void ReadRecentProjects(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("'recentProjects' must be an array.");

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            string? path = null;
            string? name = null;
            DateTime opened = DateTime.MinValue;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("path"))
                {
                    reader.Read();
                    path = reader.GetString();
                }
                else if (reader.ValueTextEquals("name"))
                {
                    reader.Read();
                    name = reader.GetString();
                }
                else if (reader.ValueTextEquals("openedUtc"))
                {
                    reader.Read();
                    DateTime.TryParse(
                        reader.GetString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out opened);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            // An incomplete entry is dropped; the rest of the list still loads.
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name)
                && _recentProjects.Count < MaxRecentProjects)
            {
                _recentProjects.Add(new RecentProject(path, name, opened));
            }
        }
    }
}
