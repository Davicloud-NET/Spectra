using System;
using System.Text;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The rule for a tag a level is built with: text of 1 to
/// <see cref="TagLimits.MaxBytes"/> bytes. A running script may also add the
/// empty tag, which no level holds.
/// </summary>
public static class TagName
{
    /// <summary>The rule as a sentence with no full stop, for whoever refuses a tag.</summary>
    public const string Rule = "A tag is 1 to 256 bytes of text";

    /// <summary>Whether a level can hold this tag.</summary>
    public static bool IsValid(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return tag.Length > 0 && TextRule.IsWellFormed(tag) && Encoding.UTF8.GetByteCount(tag) <= TagLimits.MaxBytes;
    }

    /// <summary>What to say of a tag that breaks the rule. A long tag is not said back.</summary>
    public static string Refusal(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Length == 0)
            return $"A tag cannot be empty. {Rule}";

        if (!TextRule.IsWellFormed(tag))
            return $"A tag has to be text a file can hold. {Rule}";

        return $"A tag of {Encoding.UTF8.GetByteCount(tag)} bytes is too long. {Rule}";
    }

    /// <summary>
    /// The order a script is handed a node's tags in, which is not the order
    /// the node got them in: by the bytes of their UTF-8, and the shorter
    /// first when one tag begins the other. Roblox hands them out sorted too.
    /// </summary>
    public static int CompareBytes(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++)
        {
            if (a[i] == b[i])
                continue;

            // UTF-16 puts half of a pair below U+E000 to U+FFFF, and UTF-8
            // puts the character the pair stands for above them.
            bool isPairInA = char.IsSurrogate(a[i]);
            return isPairInA == char.IsSurrogate(b[i]) ? a[i].CompareTo(b[i]) : isPairInA ? 1 : -1;
        }

        return a.Length.CompareTo(b.Length);
    }
}
