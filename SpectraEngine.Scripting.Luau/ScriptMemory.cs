using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// What the scripts of one state hold against the memory budget, and the stop
// for a script that goes past it. The shim counts Luau's bytes and the
// weights of userdata. The bytes the host keeps for the scripts on nodes are
// written to it from here, with no call.
internal sealed unsafe class ScriptMemory
{
    private const long Megabyte = 1024 * 1024;

    private readonly LuauHost _host;
    private readonly nint _main;
    private readonly HostCells* _cells;
    private long _budget;
    private long _nodeBytes;
    private string? _heldAtStop;

    public ScriptMemory(LuauHost host, nint main, long budget)
    {
        _host = host;
        _main = main;
        _cells = Shim.sl_cells(main);
        Budget = budget;
    }

    public long Budget
    {
        get => _budget;
        set
        {
            _budget = value;
            if (!_host.IsClosed)
                Shim.sl_setbudget(_main, value);
        }
    }

    // What the host keeps for the scripts outside any userdata, such as the
    // attributes they set on nodes. It counts against the budget.
    public long NodeBytes
    {
        get => _nodeBytes;
        set
        {
            _nodeBytes = value;
            if (!_host.IsClosed)
                Volatile.Write(ref _cells->Live, value);
        }
    }

    // Of NodeBytes, about what is on made trees in no level.
    public long LooseBytes
    {
        set
        {
            if (!_host.IsClosed)
                _cells->Loose = value;
        }
    }

    // The interrupt let a script go on that the loose trees alone put past
    // the budget, and wants them looked over.
    public bool IsLookOverWanted => !_host.IsClosed && _cells->IsLookOverWanted != 0;

    // What counts against the budget now, garbage included.
    public long Held => _host.IsClosed ? 0 : Shim.sl_held(_main);

    public bool IsOver => _budget > 0 && Held > _budget;

    // Said before the loose trees are looked over. Any userdata collected
    // from here on may leave one of them held by no script again.
    public void LookingOver()
    {
        if (_host.IsClosed)
            return;

        _cells->IsLooseUnsure = 0;
        _cells->IsLookOverWanted = 0;
    }

    // What a host function returns that found the scripts over the budget.
    // The script stops as the interrupt would stop it, so a pcall does not
    // hold on, and is switched off.
    public int Fail(LuauStack stack)
    {
        Stop();
        return stack.Fail(OverBudget(_heldAtStop ?? string.Empty));
    }

    // The stop alone, for whoever takes back what went over before it fails.
    // What was held is kept for the message from here.
    public void Stop()
    {
        if (!_host.IsClosed)
            Shim.sl_stopcall(_main, Shim.StopMemory);

        _heldAtStop ??= HeldOutsideLuau();
    }

    // What the sink is told for a script the host stopped for memory, or
    // null for any other stop. What a stop kept goes either way.
    public string? Explain(int stop)
    {
        string? held = _heldAtStop;
        _heldAtStop = null;
        return stop == Shim.StopMemory ? OverBudget(held ?? HeldOutsideLuau()) : null;
    }

    private string OverBudget(string held)
    {
        string budget = _budget >= Megabyte ? $"{_budget / Megabyte} MB" : $"{_budget / 1024} KB";
        return $"the scripts hold more than {budget}{held} and this one was switched off";
    }

    // What the host keeps for the scripts counts too, and a script that reads
    // only Luau's own count would not find it. The larger of the two is named.
    private string HeldOutsideLuau()
    {
        long weights = _host.IsClosed ? 0 : Shim.sl_held(_main) - Shim.sl_memory(_main) - _nodeBytes;
        if (weights <= 0 && _nodeBytes <= 0)
            return string.Empty;

        return weights >= _nodeBytes
            ? ", with what the destroyed nodes they keep hold,"
            : ", with the tags and attributes they set on nodes,";
    }
}
