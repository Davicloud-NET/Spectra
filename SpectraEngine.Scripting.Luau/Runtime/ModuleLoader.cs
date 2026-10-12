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
    // Roblox's words for the two.
    public const string InvalidArgument = "Attempted to call require with invalid argument(s).";
    public const string Recursive = "Requested module was required recursively";

    // How many modules may run at once, each asked for by the one before.
    public const int MaxDepth = 100;

    private readonly ModuleRunner _runner = new(host, sink, waiting);
    private readonly Dictionary<string, ScriptModule> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SceneNode, ScriptModule> _byNode = new(ReferenceEqualityComparer.Instance);
    private readonly ModuleAsks _asks = new();

    // The modules whose files are running, each asked for by the one before it.
    private readonly List<ScriptModule> _running = [];

    /// <summary>
    /// Gives what a module returns. The module is a file named by a path, such as ./lib/util or
    /// @assets/Shared/util, or a module script. It runs once while the level plays.
    /// </summary>
    [ScriptFunction(Name = "require", Parameters = "module: string | Instance", Returns = "any")]
    private int Require(LuauStack stack)
    {
        if (stack.TypeOf(1) == LuauType.String && stack.TryGetUtf8(1, out ReadOnlySpan<byte> asked))
            return ByPath(stack, asked);

        return host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? target) switch
        {
            LuauObjectLookup.Released => stack.Fail(ScriptMember.HasLeft),
            LuauObjectLookup.Found when target is SceneNode node => ByNode(stack, node),
            LuauObjectLookup.Found when target is ScriptObject other => stack.Fail(NotAModule(other.ClassName)),
            _ => stack.Fail(NotAModule(ScriptArguments.TypeName(stack, 1))),
        };
    }

    // Both spans are Luau's own bytes. They are read before the module runs.
    private int ByPath(LuauStack stack, ReadOnlySpan<byte> askedBytes)
    {
        ReadOnlySpan<byte> asker = ScriptReport.CallerFile(stack.State);
        if (_asks.Find(asker, askedBytes) is { } again)
            return Give(stack, again);

        string asked = Encoding.UTF8.GetString(askedBytes);
        if (!ModulePath.TryResolve(asked, Encoding.UTF8.GetString(asker), out string[] files, out string? refusal))
            return stack.Fail(refusal);

        if (Known(files) is { } known)
        {
            _asks.Add(asker, askedBytes, known);
            return Give(stack, known);
        }

        if (TooDeep() is { } deep)
            return stack.Fail(deep);
        if (FindFile(asked, files, out string path, out byte[] text) is { } noFile)
            return stack.Fail(noFile);

        ScriptModule module = new(path, node: null);
        _byPath[path] = module;
        _asks.Add(asker, askedBytes, module);
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

    // The module one of the files already is. The table ignores case, as a
    // pack and a Windows folder do. Where the source tells case apart, a
    // file in another case is not there, so it is no module either, whoever
    // asked first.
    private ScriptModule? Known(string[] files)
    {
        foreach (string file in files)
        {
            if (_byPath.TryGetValue(file, out ScriptModule? known) && (known.Path == file || IsThere(file)))
                return known;
        }

        return null;
    }

    private bool IsThere(string file) => source.Read(file, out string? problem) is not null || problem is not null;

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

        return stack.Fail(_running.Contains(module) ? Circle(module) : _runner.Refusal(module));
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
            _runner.Run(module, text);
        }
        finally
        {
            _running.Remove(module);
        }

        if (module.Value is null && !module.IsKept)
            Forget(module);

        return Give(stack, module);
    }

    // The next ask reads the module's file and runs it again.
    private void Forget(ScriptModule module)
    {
        if (module.Node is { } node)
            _byNode.Remove(node);
        else
            _byPath.Remove(module.Path);

        _asks.Clear();
    }

    private static string NotAModule(string got) =>
        $"{InvalidArgument} A path or a ModuleScript was expected, got {got}";
}
