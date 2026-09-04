namespace SpectraEngine.Editor.Shell;

/// <summary>
/// How well a typed query matches a command's name: a subsequence match, scored
/// so that word starts and runs beat scattered letters.
/// </summary>
/// <remarks>
/// <para>
/// A pure type with no Avalonia in it, because the ranking is the half of a
/// palette that is worth testing and the half that is easiest to get subtly
/// wrong. "ib" should reach "Insert block" ahead of "Grid: alw<b>a</b>ys", and
/// no amount of looking at a list tells you whether it does.
/// </para>
/// <para>
/// Subsequence rather than substring, because a palette's whole value is that
/// you type the letters you remember rather than a prefix you do not.
/// </para>
/// </remarks>
public static class CommandScore
{
    /// <summary>A query that matched nothing.</summary>
    public const int NoMatch = -1;

    /// <summary>
    /// Scores <paramref name="candidate"/> against <paramref name="query"/>, or
    /// <see cref="NoMatch"/>. Higher is better; ties are the caller's to break.
    /// </summary>
    public static int Of(string candidate, string query)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Length == 0)
        {
            return 0;
        }

        int score = 0;
        int at = 0;
        int run = 0;

        foreach (char wanted in query)
        {
            if (char.IsWhiteSpace(wanted))
            {
                continue;
            }

            // A WORD START IS PREFERRED OVER THE NEAREST LETTER, because a
            // greedy scan takes the wrong one: "fe" matched the e of "Frame"
            // rather than the one starting "everything", so "Finer grid" beat
            // "Frame everything" at its own initials. Falling back to the first
            // occurrence keeps plain subsequences ("snap") working.
            int found = IndexOf(candidate, wanted, at, wordStartOnly: true);
            if (found < 0)
            {
                found = IndexOf(candidate, wanted, at, wordStartOnly: false);
            }

            if (found < 0)
            {
                return NoMatch;
            }

            // A letter that starts a word is what a person actually remembers,
            // so "ib" reaching "Insert block" has to beat the same two letters
            // scattered through a longer name.
            bool startsWord = StartsWord(candidate, found);
            run = found == at ? run + 1 : 0;

            score += 10;
            score += startsWord ? 18 : 0;
            score += run * 6;
            score -= Math.Min(found - at, 6);

            at = found + 1;
        }

        // A short name that matched is a better answer than a long one that
        // matched the same way: "Undo" over "Insert an unlit decal".
        return score - (candidate.Length / 8);
    }

    private static int IndexOf(string text, char wanted, int from, bool wordStartOnly)
    {
        for (int i = from; i < text.Length; i++)
        {
            if (char.ToUpperInvariant(text[i]) != char.ToUpperInvariant(wanted))
            {
                continue;
            }

            if (!wordStartOnly || StartsWord(text, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool StartsWord(string text, int at) =>
        at == 0 || text[at - 1] is ' ' or ':' or '-';
}
