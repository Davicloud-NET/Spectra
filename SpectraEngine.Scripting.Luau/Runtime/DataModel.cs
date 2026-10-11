using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The global `game`: where a script asks for a service, and the way to the
// level's root as Workspace.
internal sealed class DataModel : ScriptObject
{
    private readonly LuauHost _host;
    private readonly SceneNode _workspace;
    private readonly RunService _runService;

    public DataModel(LuauHost host, SceneNode workspace, RunService runService)
    {
        _host = host;
        _workspace = workspace;
        _runService = runService;

        Methods = new ApiTable(host)
            .Function("GetService"u8, GetService)
            .KeepAsMethods();
    }

    public override string ClassName => "DataModel";

    public override string Name => "game";

    public override MethodTable Methods { get; }

    public override int ReadProperty(LuauStack stack, ReadOnlySpan<byte> name) =>
        name.SequenceEqual("Workspace"u8) ? Push(stack, _workspace) : NoSuchProperty;

    private int GetService(LuauStack stack)
    {
        if (_host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? self) != LuauObjectLookup.Found
            || !ReferenceEquals(self, this))
        {
            return ScriptArguments.NoSelf(stack, "GetService");
        }

        // The name is compared where Luau keeps it, so asking makes no string.
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> service))
            return ScriptArguments.Wrong(stack, 2, "GetService", "string", isMethod: true);
        if (service.SequenceEqual("RunService"u8))
            return Push(stack, _runService);
        if (service.SequenceEqual("Workspace"u8))
            return Push(stack, _workspace);

        return stack.Fail($"'{stack.GetText(2)}' is not a valid Service name");
    }

    private int Push(LuauStack stack, object value) =>
        ScriptResult.Of(_host.Objects.Push(stack, ScriptTags.Object, value));
}
