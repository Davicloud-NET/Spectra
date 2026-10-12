using System;

namespace SpectraEngine.Core.Entities;

// One kind of thing a running level changes on something that outlives it:
// how to read what the owner holds, and how to put a first value back at Stop.
internal class JournalKind<TOwner, T>(Func<TOwner, T> read, Action<TOwner, T> putBack)
    where TOwner : class
{
    public T Read(TOwner owner) => read(owner);

    public void PutBack(TOwner owner, T first) => putBack(owner, first);
}
