using Avalonia.Interactivity;
using Avalonia.Threading;
using SpectraEngine.Core.Assets;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Scripts;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace SpectraEngine.Editor;

// What the window does with a script before the level plays: makes one,
// attaches one that is there, and opens one in a code editor.
public partial class MainWindow
{
    // The one way a script is opened, whoever asks.
    private readonly ScriptLauncher _scripts = ScriptLauncher.ForThisComputer();

    private void OnInsertScriptClicked(object? sender, RoutedEventArgs e) => InsertScript();

    // The file and the node are made on the render thread, where the
    // selection is the live one. This thread's copy is a frame or two behind,
    // and a file named after the wrong node stays on disk.
    private void InsertScript()
    {
        if (_session is not { } session)
            return;

        if (_document.Project is not { } project)
        {
            _shell.SetWarning(
                "Open a project before adding a script. Its file is made in the project's Assets folder.");
            return;
        }

        session.InsertScript(
            project.AssetsPath,
            ScriptTemplate.Read(),
            report => Dispatcher.UIThread.Post(() => OnScriptAdded(session, report)));
    }

    // A script that is there, put under a node, under what a point in the
    // viewport hits, or with neither under the selection.
    private void AttachScript(EditorSession session, string contentPath, Guid? parentId, Vector2? point) =>
        session.AttachScript(
            contentPath, parentId, point,
            report => Dispatcher.UIThread.Post(() => OnScriptAdded(session, report)));

    private void OnScriptAdded(EditorSession session, ScriptInsertReport report)
    {
        if (!report.Added)
        {
            _shell.SetWarning(report.Describe());
            return;
        }

        _shell.SetMessage(report.Describe());

        // A new file is there to be written in. The session may be gone by
        // now, and then its project is not the one to open a window on.
        if (report.MadeFile && ReferenceEquals(session, _session))
            OpenScript(report.ContentPath, keepStatus: true);
    }

    private void OnAssetDroppedOnNode(ContentDragPayload payload, Guid nodeId)
    {
        if (AssetDropPolicy.RefuseOnNode(payload, _session is not null, _shell.IsPlaying) is { } refusal)
        {
            _shell.SetWarning(refusal);
            return;
        }

        if (_session is { } session)
            AttachScript(session, payload.ContentPath, nodeId, null);
    }

    // The Content panel's menu. A script goes under the selected node. Any
    // other file does what a double click on it does.
    private void OnContentInsertRequested(ContentEntry entry)
    {
        if (!entry.IsScript)
        {
            OnContentActivated(entry);
            return;
        }

        if (_session is not { } session)
        {
            _shell.SetWarning("Open a project before inserting or assigning anything.");
            return;
        }

        if (_shell.Content is not { } browser || !browser.TryDescribe(entry, out ContentDragPayload? payload))
        {
            _shell.SetWarning($"{entry.Name} is not inside this project's Assets folder.");
            return;
        }

        AttachScript(session, payload.ContentPath, null, null);
    }

    // The node's script is asked of the engine: the tree knows that a node
    // carries one and not which file it names.
    private void OpenScriptOf(Guid nodeId)
    {
        if (_session is not { } session)
            return;

        session.FindScript(nodeId, path => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(session, _session))
                return;

            if (path is null)
                _shell.SetWarning("That node carries no script any more.");
            else
                OpenScript(path);
        }));
    }

    // Opens a script by the path a level stores for it. A line, counted from
    // 1, is where the caret goes: an error in Output knows its line.
    private void OpenScript(string contentPath, bool keepStatus = false, int? line = null)
    {
        // A level opened by itself reads the engine's own content folder.
        string root = _document.Project?.AssetsPath ?? ContentRoot.Path;

        string full;
        try
        {
            full = ContentRoot.ResolveAbsolute(root, contentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or NotSupportedException)
        {
            _shell.SetError($"'{contentPath}' is not a path inside the Assets folder.");
            return;
        }

        _ = OpenScriptFileAsync(full, keepStatus, line);
    }

    // The start runs off this thread and the answer comes back to it.
    private async Task OpenScriptFileAsync(string fullPath, bool keepStatus = false, int? line = null)
    {
        ScriptOpenOutcome outcome = await _scripts.OpenAsync(
            new ScriptOpenRequest(fullPath, _document.Project?.Root, line));

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
}
