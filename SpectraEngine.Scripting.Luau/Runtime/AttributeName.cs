namespace SpectraEngine.Scripting.Luau.Runtime;

// The rule for the name of an attribute a script sets, which is Roblox's:
// letters, digits and the underscore, at most a hundred, and no RBX in front.
internal static class AttributeName
{
    public const int MostCharacters = 100;

    private const string Rule =
        "An attribute's name is 1 to 100 letters, digits and underscores, and does not start with RBX";

    // What a script reads when the bytes it handed over are no text at all.
    public const string NotText = "A name that is not valid UTF-8 is not a valid attribute name. " + Rule;

    public static bool IsValid(ReadOnlySpan<char> name)
    {
        if (name.Length is 0 or > MostCharacters || name.StartsWith("RBX", StringComparison.Ordinal))
            return false;

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    // What a script reads for a name that breaks the rule. A long one is not
    // said back.
    public static string Refusal(ReadOnlySpan<char> name) =>
        name.Length > MostCharacters
            ? $"A name of {name.Length} characters is not a valid attribute name. {Rule}"
            : $"'{name}' is not a valid attribute name. {Rule}";
}
