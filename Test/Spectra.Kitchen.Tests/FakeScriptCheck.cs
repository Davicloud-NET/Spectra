using SpectraEngine.Core.Scripting;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace Spectra.Kitchen.Tests;

// A script check with no Luau behind it. It refuses a script that holds the
// word in Refused, at the line and column the word stands at, and keeps what
// it was asked. A cook asks from several threads.
internal sealed class FakeScriptCheck : IScriptSyntaxCheck
{
    public const string Refused = "oops";

    private readonly ConcurrentQueue<byte[]> _asked = new();

    public string CompilerVersion { get; init; } = "fake 1";

    public string? Problem { get; init; }

    // False for a compiler that names a line and no column, as Luau does.
    public bool NamesColumns { get; init; } = true;

    // Every text it was asked about, in the order the calls came.
    public byte[][] Asked => [.. _asked];

    public ScriptSyntaxError? Check(ReadOnlySpan<byte> source)
    {
        _asked.Enqueue(source.ToArray());

        int at = source.IndexOf(Encoding.UTF8.GetBytes(Refused));
        if (at < 0) return null;

        ReadOnlySpan<byte> before = source[..at];
        int line = 1 + before.Count((byte)'\n');
        int column = at - before.LastIndexOf((byte)'\n');

        return new ScriptSyntaxError(line, NamesColumns ? column : 0, $"'{Refused}' is no statement");
    }
}
