using System.ComponentModel;

namespace System.Runtime.CompilerServices;

// Polyfill: records need this for init accessors and netstandard2.0 lacks it.
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
