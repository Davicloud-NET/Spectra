using System;

namespace SpectraEngine.Core.ConsoleSystem;

// A semicolon ends a command and // starts a comment, except inside double
// quotes. Quotes group one token, and \" and \\ are the only escapes in them.
internal static class ConsoleTokenizer
{
    // Finds the next command at or after position and moves position past it.
    // False when the line holds no more.
    public static bool TryNextCommand(ReadOnlySpan<char> line, ref int position, out ReadOnlySpan<char> command)
    {
        while (position < line.Length)
        {
            int end = FindCommandEnd(line, position);
            command = line[position..end].Trim();

            // Anything but a semicolon here is a comment, which takes the rest of the line.
            position = end < line.Length && line[end] == ';' ? end + 1 : line.Length;

            if (!command.IsEmpty)
                return true;
        }

        command = default;
        return false;
    }

    // Writes the tokens to text without their quotes and escapes, and one range
    // into text per token. Both buffers must be as long as the command.
    // Returns how many tokens there are. The first is the command's name.
    public static int Tokenize(
        ReadOnlySpan<char> command,
        Span<char> text,
        Span<Range> tokens,
        out ReadOnlySpan<char> rawArgs,
        out bool quoteLeftOpen)
    {
        int count = 0;
        int written = 0;
        int nameEnd = command.Length;
        int i = 0;
        quoteLeftOpen = false;

        while (true)
        {
            while (i < command.Length && char.IsWhiteSpace(command[i]))
                i++;
            if (i == command.Length)
                break;

            int tokenStart = written;
            if (command[i] == '"')
            {
                i++;
                quoteLeftOpen = true;
                while (i < command.Length)
                {
                    if (command[i] == '"')
                    {
                        quoteLeftOpen = false;
                        i++;
                        break;
                    }

                    if (IsEscape(command, i))
                        i++;
                    text[written++] = command[i++];
                }
            }
            else
            {
                // A quote ends a bare token and starts the next one.
                while (i < command.Length && !char.IsWhiteSpace(command[i]) && command[i] != '"')
                    text[written++] = command[i++];
            }

            tokens[count++] = tokenStart..written;
            if (count == 1)
                nameEnd = i;
        }

        rawArgs = command[nameEnd..].Trim();
        return count;
    }

    private static int FindCommandEnd(ReadOnlySpan<char> line, int start)
    {
        bool quoted = false;
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (IsEscape(line, i))
                    i++;
                else if (c == '"')
                    quoted = false;
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ';' || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
            {
                return i;
            }
        }

        return line.Length;
    }

    private static bool IsEscape(ReadOnlySpan<char> text, int index) =>
        text[index] == '\\' && index + 1 < text.Length && text[index + 1] is '"' or '\\';
}
