using SpectraEngine.Core.Assets;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Scripting.Tooling;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace SpectraEngine.Editor;

/// <summary>
/// What the window does with a script: makes one, attaches one that is
/// there, and opens one in a code editor, at a line when one is named.
/// </summary>
internal sealed class ScriptCommands
{
    private readonly ShellModel _shell;
    private readonly EditorDocument _document;
    private readonly Func<EditorSession?> _session;
    private readonly ScriptLauncher _launcher;
    private readonly Action<Action> _post;

    // The opens that have not answered yet, each by its file and its line.
    private readonly HashSet<(string File, int? Line)> _opening = [];

    // The last line about files a project's folder did not take.
    private string? _refusalSaid;

    /// <summary>Creates the commands.</summary>
    /// <param name="session">The session that is live now, or null.</param>
    /// <param name="launcher">The one way a script is opened, whoever asks.</param>
    /// <param name="post">Runs an answer from the engine on the UI thread.</param>
    public ScriptCommands(
        ShellModel shell, EditorDocument document, Func<EditorSession?> session,
        ScriptLauncher launcher, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(post);

        _shell = shell;
        _document = document;
        _session = session;
        _launcher = launcher;
        _post = post;

        // A line in Output that names a script and a line opens it there.
        shell.Output.PlaceRequested += OpenPlace;

        document.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(EditorDocument.Project))
                return;

            // Another project is another folder, with refusals of its own.
            _refusalSaid = null;
            _ = RefreshProjectAsync();
        };
    }

    /// <summary>
    /// Brings the open project's definitions file up to this build, when the
    /// project has one. Off the calling thread: it reads and writes the disk.
    /// </summary>
    // A project last opened with an older engine holds an older script API,
    // and a code editor that is open on it reads the file with no script
    // opened from here.
    public async Task RefreshProjectAsync()
    {
        if (_document.Project is not { } project)
            return;

        Say(await Task.Run(() => _launcher.Setup.Refresh(project.Root)));
    }

    /// <summary>Makes a script file and adds a script that names it, under the selection.</summary>
    // The file and the node are made on the render thread, where the
    // selection is the live one. This thread's copy is a frame or two behind,
    // and a file named after the wrong node stays on disk.
    public void InsertNew()
    {
        if (_document.Project is not { } project)
        {
            _shell.SetWarning(
                "Open a project before adding a script. Its file is made in the project's Assets folder.");
            return;
        }

        if (_session() is not { } session)
            return;

        session.InsertScript(
            project.AssetsPath,
            ScriptTemplate.Read(),
            report => _post(() => OnAdded(session, report)));
    }

    /// <summary>
    /// Adds a script that is there under a node, under what a point in the
    /// viewport hits, or with neither under the selection.
    /// </summary>
    public void Attach(EditorSession session, string contentPath, Guid? parentId, Vector2? point)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.AttachScript(contentPath, parentId, point, report => _post(() => OnAdded(session, report)));
    }

    /// <summary>Adds a file dropped on a row of the scene tree under that row's node.</summary>
    public void AttachDropped(ContentDragPayload payload, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(payload);

        EditorSession? session = _session();
        if (AssetDropPolicy.RefuseOnNode(payload, session is not null, _shell.IsPlaying) is { } refusal)
        {
            _shell.SetWarning(refusal);
            return;
        }

        if (session is not null)
            Attach(session, payload.ContentPath, nodeId, null);
    }

    /// <summary>Adds a script picked in the Content panel under the selected node.</summary>
    public void AttachListed(ContentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_session() is not { } session)
        {
            _shell.SetWarning("Open a project before inserting or assigning anything.");
            return;
        }

        if (_shell.Content is not { } browser || !browser.TryDescribe(entry, out ContentDragPayload? payload))
        {
            _shell.SetWarning($"{entry.Name} is not inside this project's Assets folder.");
            return;
        }

        Attach(session, payload.ContentPath, null, null);
    }

    /// <summary>Opens the script a node carries.</summary>
    // Asked of the engine: the tree knows that a node carries a script and
    // not which file it names.
    public void OpenOf(Guid nodeId)
    {
        if (_session() is not { } session)
            return;

        session.FindScript(nodeId, path => _post(() =>
        {
            if (!ReferenceEquals(session, _session()))
                return;

            if (path is null)
                _shell.SetWarning("That node carries no script any more.");
            else
                _ = OpenAsync(path);
        }));
    }

    /// <summary>Opens the script a line in Output came from, at that line.</summary>
    public void OpenPlace(ConsolePlace place) =>
        _ = OpenAsync(place.File, line: place.Line > 0 ? place.Line : null);

    /// <summary>Opens a script by the path a level stores for it.</summary>
    /// <param name="keepStatus">Whether a plain "Opened" goes to Output and leaves the status bar.</param>
    /// <param name="line">Where the caret goes, counted from 1, or null for the top.</param>
    public Task OpenAsync(string contentPath, bool keepStatus = false, int? line = null)
    {
        // A level opened by itself reads the engine's own content folder.
        string root = _document.Project?.AssetsPath ?? ContentRoot.Path;

        string full;
        try
        {
            full = ContentRoot.ResolveAbsolute(root, contentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            _shell.SetError($"'{contentPath}' is not a path inside the Assets folder.");
            return Task.CompletedTask;
        }

        return OpenFileAsync(full, keepStatus, line);
    }

    /// <summary>Opens a script by its full path. The start runs off the calling thread.</summary>
    public async Task OpenFileAsync(string fullPath, bool keepStatus = false, int? line = null)
    {
        // A second click on a row whose program is still starting would start
        // it again. Another file or another line is another thing to open.
        (string File, int? Line) asked = (fullPath, line);
        if (!_opening.Add(asked))
        {
            _shell.SetMessage($"{Path.GetFileName(fullPath)} is still being opened.");
            return;
        }

        ScriptOpenOutcome outcome;
        try
        {
            outcome = await _launcher.OpenAsync(new ScriptOpenRequest(fullPath, _document.Project?.Root, line));
        }
        finally
        {
            _opening.Remove(asked);
        }

        Say(outcome.Project);

        foreach (string failure in outcome.Failures)
            _shell.Output.Append(OutputSeverity.Warning, failure);

        // After Insert Script the status bar says where the file was made.
        // "Opened" would take that line away, so it goes to Output alone.
        if (keepStatus && outcome.Severity == OutputSeverity.Info)
        {
            _shell.Output.Append(OutputSeverity.Info, outcome.Message);
            return;
        }

        switch (outcome.Severity)
        {
            case OutputSeverity.Error: _shell.SetError(outcome.Message); break;
            case OutputSeverity.Warning: _shell.SetWarning(outcome.Message); break;
            default: _shell.SetMessage(outcome.Message); break;
        }
    }

    // One line in Output for what was written into the project's folder, or
    // for what could not be. Never the status bar: the open has that.
    private void Say(ScriptProjectReport report)
    {
        string? wrote = report.DescribeWritten();
        string? refused = report.DescribeRefused();
        if (wrote is null && refused is null)
            return;

        // A folder that does not take a file refuses it on every open, so
        // a refusal is said when it is new.
        string? news = refused == _refusalSaid ? null : refused;
        _refusalSaid = refused;

        string?[] parts = [wrote, news];
        string line = string.Join(' ', parts.Where(part => part is not null));
        if (line.Length > 0)
            _shell.Output.Append(news is null ? OutputSeverity.Info : OutputSeverity.Warning, line);
    }

    private void OnAdded(EditorSession session, ScriptInsertReport report)
    {
        if (!report.Added)
        {
            _shell.SetWarning(report.Describe());
            return;
        }

        _shell.SetMessage(report.Describe());

        // A new file is there to be written in. The session may be gone by
        // now, and then its project is not the one to open a window on.
        if (report.MadeFile && ReferenceEquals(session, _session()))
            _ = OpenAsync(report.ContentPath, keepStatus: true);
    }
}
