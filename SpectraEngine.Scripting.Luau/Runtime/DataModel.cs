using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The global `game`: where a script asks for a service, and the way to the
// level's root as Workspace.
internal sealed class DataModel : ScriptObject
{
    private const string WorkspaceName = "Workspace";

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

        return stack.GetText(2) switch
        {
            RunService.ServiceName => Push(stack, _runService),
            WorkspaceName => Push(stack, _workspace),
            null => ScriptArguments.Wrong(stack, 2, "GetService", "string", isMethod: true),
            string other => stack.Fail($"'{other}' is not a valid Service name"),
        };
    }

    private int Push(LuauStack stack, object value) =>
        ScriptResult.Of(_host.Objects.Push(stack, ScriptTags.Object, value));
}
