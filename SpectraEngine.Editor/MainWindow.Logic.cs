using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Cameras;
using SpectraEngine.Editor.Shell;
using SpectraEngine.Editor.Shell.Logic;
using System;

namespace SpectraEngine.Editor;

// The Logic view in its pane. One model for the window's life: the pane grid
// moves between a dock tool and a grid cell when a session restarts, and the
// view must come back as it was.
public partial class MainWindow
{
    private readonly LogicViewModel _logic = new();

    private void CreateLogicView()
    {
        var view = new LogicView { Model = _logic };
        view.SelectRequested += OnLogicSelectRequested;
        view.FrameRequested += OnLogicFrameRequested;
        LogicPaneBody.Child = view;

        _logic.ShownEntitiesChanged += SendLogicRequest;
    }

    private void OnLogicSelectRequested(Guid nodeId, bool additive) =>
        _session?.Select(nodeId, additive ? SelectionUpdate.Add : SelectionUpdate.Replace);

    // Select and frame sit in one ordered queue, as a double click in the
    // scene tree does.
    private void OnLogicFrameRequested(Guid nodeId)
    {
        _session?.Select(nodeId);
        _session?.Post(EditorCameraCommand.FrameSelection);
    }

    // The engine publishes a level's wiring only while a view asks for it.
    // Sent when the pane shows or hides, when what it shows changes, and to
    // every new session, which starts out publishing nothing.
    private void SendLogicRequest()
    {
        if (_session is not { } session)
            return;

        session.Host.RequestLogicView(_viewArrangement == ViewArrangement.Single
            ? LogicViewRequest.Hidden
            : new LogicViewRequest(_logic.ShownEntityIds));
    }
}
