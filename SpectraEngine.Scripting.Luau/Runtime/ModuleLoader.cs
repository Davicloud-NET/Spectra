using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Gives a script the modules it asks for. A module's file runs once in a
// play session, the first time it is asked for, on a thread and with globals
// of its own. Everyone who asks gets the one value it returned.
[ScriptGlobals]
internal sealed partial class ModuleLoader(
    LuauHost host, IScriptSource source, NodeNamingSink sink, WaitingThreads waiting)
{
    // Roblox's words for the four.
    public const string InvalidArgument = "Attempted to call require with invalid argument(s).";
    public const string NotOneValue = "Module code did not return exactly one value";
    public const string Recursive = "Requested module was required recursively";
    public const string LoadFailed = "Requested module experienced an error while loading";

    public const string Waited =
        "a module cannot wait before it returns. Start a thread with task.spawn for what has to wait";

    // How many modules may run at once, each asked for by the one before.
    public const int MaxDepth = 100;

    private readonly Dictionary<string, ScriptModule> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SceneNode, ScriptModule> _byNode = new(ReferenceEqualityComparer.Instance);

    // The modules whose files are running, each asked for by the one before it.
    private readonly List<ScriptModule> _running = [];

    /// <summary>
    /// Gives what a module returns. A path starts with ./ or ../ from the file that asks, or with
    /// @self or @assets, and names x.luau or x/init.luau without the extension. A node has to be a
    /// module script. A module runs once while the level plays, the first time it is asked for, and
    /// everyone gets the same value. It has to return one value and cannot wait before it does.
    /// Its script is its node when it was asked for by node, and nil when it was asked for by path.
    /// </summary>
    [ScriptFunction(Name = "require", Parameters = "module: string | Instance", Returns = "any")]
    private int Require(LuauStack stack)
    {
        if (stack.TypeOf(1) == LuauType.String && stack.GetText(1) is { } asked)
            return ByPath(stack, asked);

        return host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? target) switch
        {
            LuauObjectLookup.Released => stack.Fail(ScriptMember.HasLeft),
            LuauObjectLookup.Found when target is SceneNode node => ByNode(stack, node),
            LuauObjectLookup.Found when target is ScriptObject other => stack.Fail(NotAModule(other.ClassName)),
            _ => stack.Fail(NotAModule(ScriptArguments.TypeName(stack, 1))),
        };
    }

    private int ByPath(LuauStack stack, string asked)
    {
        string asker = ScriptReport.CallerFile(stack.State, host.ScriptOf(stack));
        if (!ModulePath.TryResolve(asked, asker, out string[] files, out string? refusal))
            return stack.Fail(refusal);

        foreach (string file in files)
        {
            if (_byPath.TryGetValue(file, out ScriptModule? known))
                return Give(stack, known);
        }

        if (TooDeep() is { } deep)
            return stack.Fail(deep);
        if (FindFile(asked, files, out string path, out byte[] text) is { } noFile)
            return stack.Fail(noFile);

        ScriptModule module = new(path, node: null);
        _byPath.Add(path, module);
        return Load(stack, module, text);
    }

    // A module node is a module of its own, also when its file was asked for
    // by path or is on another node: each has its own script.
    private int ByNode(LuauStack stack, SceneNode node)
    {
        if (node.Script is not { IsModule: true } script)
            return stack.Fail($"{InvalidArgument} '{node.Name}' ({NodeClassName.Of(node)}) is not a ModuleScript");
        if (_byNode.TryGetValue(node, out ScriptModule? known))
            return Give(stack, known);
        if (!script.IsEnabled)
            return stack.Fail($"the module '{node.Name}' is switched off");
        if (TooDeep() is { } deep)
            return stack.Fail(deep);

        if (source.Read(script.Path, out string? problem) is not { } text)
        {
            return stack.Fail(problem is null
                ? $"the module '{node.Name}' names {script.Path}, and the level's content has no such file"
                : $"the module '{node.Name}' names {script.Path}, which cannot be read. {problem}");
        }

        ScriptModule module = new(script.Path, node);
        _byNode.Add(node, module);
        return Load(stack, module, text);
    }

    // The one file of them that is there, with its text. Otherwise the
    // refusal: none is there, both are, or the one cannot be read.
    private string? FindFile(string asked, string[] files, out string path, out byte[] text)
    {
        path = string.Empty;
        text = [];

        string? found = null;
        byte[]? read = null;
        string? problem = null;
        foreach (string file in files)
        {
            byte[]? bytes = source.Read(file, out string? why);
            if (bytes is null && why is null)
                continue;
            if (found is not null)
                return ModulePath.TwoFiles(asked, found, file);

            (found, read, problem) = (file, bytes, why);
        }

        if (found is null)
            return ModulePath.Missing(asked, files);
        if (read is null)
            return ModulePath.Unreadable(asked, found, problem ?? string.Empty);

        path = found;
        text = read;
        return null;
    }

    // A module that runs sits on the native stack of the one that asked for
    // it, and that stack is what a chain of them would use up.
    private string? TooDeep() => _running.Count < MaxDepth
        ? null
        : $"modules ask for one another more than {MaxDepth} deep, from {_running[0].Path} down";

    private int Give(LuauStack stack, ScriptModule module)
    {
        if (module.Value is { } value)
            return ScriptResult.Of(value.Push(stack));

        return stack.Fail(_running.Contains(module) ? Circle(module) : $"{LoadFailed}: {module.Failure}");
    }

    // From the module that was asked for again to the one that asks, and
    // back to the first.
    private string Circle(ScriptModule module)
    {
        var chain = new StringBuilder(Recursive).Append(": ");
        for (int i = _running.IndexOf(module); i < _running.Count; i++)
            chain.Append(_running[i].Path).Append(" -> ");

        return chain.Append(module.Path).ToString();
    }

    private int Load(LuauStack stack, ScriptModule module, byte[] text)
    {
        _running.Add(module);
        try
        {
            if (Run(module, text) is { } failure)
                module.Failure = failure;
        }
        finally
        {
            _running.Remove(module);
        }

        return Give(stack, module);
    }

    // Null when the module returned its one value, which it then keeps.
    // Otherwise how it failed, which the sink has been told.
    private string? Run(ScriptModule module, byte[] text)
    {
        sink.TakeError();
        sink.Loading = module.Node;
        LuauScript? script = host.Load(module.Path, text);
        sink.Loading = null;

        // The host has reported a module that did not compile.
        if (script is null)
            return Heard(module);
        if (module.Node is { } node && !script.SetGlobal("script"u8, LuauValue.Object(ScriptTags.Object, node)))
            return Tell(Error(module, "the module could not be given its node"));

        return script.Start() switch
        {
            LuauResume.Finished => Keep(module, script.Thread),
            LuauResume.Yielded => Tell(EndWait(script)),
            LuauResume.Failed => Heard(module),
            _ => Tell(Error(module, "the module could not be started")),
        };
    }

    private string? Keep(ScriptModule module, LuauThread thread)
    {
        if (thread.ValueCount != 1)
            return Tell(Error(module, NotOneValue));

        module.Value = host.Keep(thread.Values, 1);
        return module.Value is null ? Tell(Error(module, "what the module returned could not be kept")) : null;
    }

    // Ends the module's thread where it waits, and its wait with it. The
    // error is at the line that waited.
    private ScriptMessage EndWait(LuauScript script)
    {
        LuauThread thread = script.Thread;
        if (!thread.IsAlive)
            return new ScriptMessage(ScriptSeverity.Error, Waited, script.Name, 0);

        ScriptMessage message = ScriptReport.Line(thread.Values.State, script, ScriptSeverity.Error, Waited);
        waiting.Remove(thread);
        thread.Cancel();
        return message;
    }

    // Reports a failure the host did not, and hands it back as an asker reads it.
    private string Tell(in ScriptMessage message)
    {
        sink.Report(in message);
        return Described(in message);
    }

    // What the host reported when the module did not compile or failed.
    private string Heard(ScriptModule module) =>
        sink.TakeError() is { } said ? Described(in said) : module.Failure;

    private static string Described(in ScriptMessage message) => $"{message.Position}: {message.Text}";

    private static ScriptMessage Error(ScriptModule module, string text) =>
        new(ScriptSeverity.Error, text, module.Path, 0);

    private static string NotAModule(string got) =>
        $"{InvalidArgument} A path or a ModuleScript was expected, got {got}";
}
