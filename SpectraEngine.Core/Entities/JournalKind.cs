using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Entities;

// The kinds of entry the play journal holds. Something new a level can
// change on a node is one more of these.
internal static class JournalKind
{
    public static readonly JournalKind<Transform> LocalTransform = new(
        static node => node.LocalTransform,
        static (node, first) => node.LocalTransform = first);
}
