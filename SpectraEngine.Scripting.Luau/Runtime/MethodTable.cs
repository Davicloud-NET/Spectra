namespace SpectraEngine.Scripting.Luau.Runtime;

// The methods of one kind of value: a table of functions the host keeps, so
// a method is one function that every value of the kind shares.
internal sealed class MethodTable(LuauReference table)
{
    // Pushes the method of that name. False when there is none, and nothing
    // is pushed then. The table stays under the method, which a host
    // function that returns its top value does not mind.
    public bool TryPush(LuauStack stack, ReadOnlySpan<byte> name)
    {
        if (!table.Push(stack))
        {
            stack.Pop();
            return false;
        }

        if (stack.GetField(-1, name) && !stack.IsNil(-1))
            return true;

        stack.Pop(2);
        return false;
    }
}
