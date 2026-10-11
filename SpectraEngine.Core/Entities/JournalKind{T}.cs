using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Core.Entities;

// One kind of thing a running level changes on a node: how to read what the
// node holds, and how to put a first value back at Stop.
internal sealed class JournalKind<T>(Func<SceneNode, T> read, Action<SceneNode, T> putBack)
{
    public T Read(SceneNode node) => read(node);

    public void PutBack(SceneNode node, T first) => putBack(node, first);
}
