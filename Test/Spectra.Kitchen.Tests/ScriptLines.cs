using SpectraEngine.Core.Scripting;
using System.Collections.Generic;
using System.Linq;

namespace Spectra.Kitchen.Tests;

// Keeps what a level's scripts report, in the order it came, each as one
// line with its kind and its place in front.
internal sealed class ScriptLines : IScriptSink
{
    private readonly List<ScriptMessage> _messages = [];

    public string[] All => [.. _messages.Select(Describe)];

    public string[] Printed => Of(ScriptSeverity.Print);

    public string[] Warnings => Of(ScriptSeverity.Warning);

    public string[] Errors => Of(ScriptSeverity.Error);

    public void Report(in ScriptMessage message) => _messages.Add(message);

    private string[] Of(ScriptSeverity severity) =>
        [.. _messages.Where(message => message.Severity == severity).Select(message => message.Text)];

    private static string Describe(ScriptMessage message) =>
        $"{message.Severity} {message.Position}: {message.Text}";
}
