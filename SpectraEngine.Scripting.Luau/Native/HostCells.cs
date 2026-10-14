using System.Runtime.InteropServices;

namespace SpectraEngine.Scripting.Luau.Native;

// HostCells in spectra_luau.cpp, which says what each field is. The binding
// writes them on the thread that runs the state, with no call.
[StructLayout(LayoutKind.Sequential)]
internal struct HostCells
{
    public long Live;
    public long Loose;
    public int IsLooseUnsure;
    public int IsLookOverWanted;
}
