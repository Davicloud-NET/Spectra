namespace SpectraEngine.Scripting.Luau.Runtime;

// What a search of the tree compares a node with.
internal enum NodeMatch : byte
{
    // The node's name.
    Name,

    // The node's own class name, and no class that one extends.
    ClassName,

    // Any class the node is of, as IsA answers.
    IsA,
}
