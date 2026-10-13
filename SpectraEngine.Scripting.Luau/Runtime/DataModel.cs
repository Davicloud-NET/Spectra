using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The level as a script reaches it, which is the global game. A script asks it for a service.</summary>
[ScriptClass("DataModel")]
internal sealed partial class DataModel : ScriptObject
{
    private readonly LuauHost _host;
    private readonly RunService _runService;
    private readonly CollectionService _collectionService;

    public DataModel(
        LuauHost host, SceneNode workspace, RunService runService, Players players, CollectionService collectionService)
    {
        _host = host;
        _runService = runService;
        _collectionService = collectionService;
        Workspace = workspace;
        Players = players;
    }

    public override string Name => "game";

    /// <summary>The root of the level's tree. The global workspace is the same node.</summary>
    [ScriptProperty]
    public SceneNode Workspace { get; }

    /// <summary>The players of the level, which is the service Players.</summary>
    [ScriptProperty]
    public Players Players { get; }

    /// <summary>Gives the service of that name: RunService, Workspace, Players or CollectionService.</summary>
    // The name is compared where Luau keeps it, so asking makes no string.
    [ScriptMethod]
    [ScriptOverload("service: \"RunService\"", "RunService")]
    [ScriptOverload("service: \"Workspace\"", "Instance")]
    [ScriptOverload("service: \"Players\"", "Players")]
    [ScriptOverload("service: \"CollectionService\"", "CollectionService")]
    private int GetService(LuauStack stack)
    {
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> service))
            return ScriptArguments.Wrong(stack, 2, "GetService", "string", isMethod: true);
        if (service.SequenceEqual("RunService"u8))
            return Push(stack, _runService);
        if (service.SequenceEqual("Workspace"u8))
            return Push(stack, Workspace);
        if (service.SequenceEqual("Players"u8))
            return Push(stack, Players);
        if (service.SequenceEqual("CollectionService"u8))
            return Push(stack, _collectionService);

        return stack.Fail($"'{stack.GetText(2)}' is not a valid Service name");
    }

    private int Push(LuauStack stack, object value) =>
        ScriptResult.Of(_host.Objects.Push(stack, ScriptTags.Object, value));
}
