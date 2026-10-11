using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A built-in class sends a value only through the generated method of an
/// output that says it sends one.
/// </summary>
// Entity.FireOutput takes any name and any text, so nothing else stops a
// class from sending what its schema does not list. This reads the sources:
// a class fires by name with no value, and only an output that sends none.
public sealed class EntityOutputSourceTests
{
    private static readonly Regex Output = new(
        @"\[EntityOutput(?:\((?<arguments>[^\]]*)\))?\]\s*public\s+const\s+string\s+(?<name>\w+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex FiredByName = new(
        @"(?<![\w.])FireOutput\((?<arguments>[^;]*)\);", RegexOptions.CultureInvariant);

    private static readonly Regex LineComment = new(@"//[^\r\n]*", RegexOptions.CultureInvariant);

    [Fact]
    public void A_class_fires_by_name_with_no_value_and_only_an_output_that_sends_none()
    {
        var wrong = new List<string>();
        int fired = 0;

        foreach (string file in EntitySources.Files())
        {
            string source = File.ReadAllText(file);
            fired += FiredByName.Matches(LineComment.Replace(source, "")).Count;

            foreach (string problem in Problems(source))
                wrong.Add($"{Path.GetFileName(file)}: {problem}");
        }

        // logic_case fires its sixteen cases by name.
        fired.ShouldBeGreaterThan(0);
        wrong.ShouldBeEmpty();
    }

    [Fact]
    public void Every_output_is_written_in_the_shape_these_tests_can_read()
    {
        // An output written another way would pass the other test unread.
        foreach (string file in EntitySources.Files())
        {
            string code = LineComment.Replace(File.ReadAllText(file), "");

            Output.Matches(code).Count.ShouldBe(
                Regex.Matches(code, @"\[EntityOutput\b").Count,
                $"{Path.GetFileName(file)}: write an output as [EntityOutput] on public const string Name");
        }
    }

    [Theory]
    [InlineData("FireOutput(OnDone, context.Activator);")]
    [InlineData("FireOutput(OnDone);")]
    [InlineData("FireOutput(OnDone, Pick(context.Activator, this));")]
    [InlineData("FireOutput(Plain[index], context.Activator);")]
    [InlineData("FireOnDone(context.Activator);\n    FireOnValue(3f);")]
    [InlineData("World.FireOutput(this, text);")]
    public void Firing_by_name_with_no_value_an_output_that_sends_none_is_fine(string statement)
    {
        Problems(Thing(statement)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("FireOutput(OnDone, context.Activator, \"5\");", "OnDone is fired by name with a value")]
    [InlineData("FireOutput(OnDone, null, context.Parameter);", "OnDone is fired by name with a value")]
    [InlineData("FireOutput(OnValue, context.Activator);", "OnValue sends a value and is fired by name")]
    [InlineData("FireOutput(Mixed[index]);", "OnValue sends a value and is fired by name")]
    [InlineData("FireOutput(\"OnDone\");", "\"OnDone\" is not an output this class declares")]
    [InlineData("FireOutput(OnOther);", "OnOther is not an output this class declares")]
    [InlineData("FireOutput(Elsewhere[index]);", "Elsewhere[index] is not an output this class declares")]
    [InlineData(
        "FireOutput(OnDone, ParameterType.Of(KeyvalueType.Float), ParameterValue.Of(3f), this);",
        "OnDone is fired by name with a value")]
    public void Firing_by_name_any_other_way_is_reported(string statement, string problem)
    {
        Problems(Thing(statement)).ShouldBe([problem]);
    }

    // What is wrong with each call to FireOutput in one class's source.
    private static List<string> Problems(string source)
    {
        string code = LineComment.Replace(source, "");

        var sendsValue = new Dictionary<string, bool>();
        foreach (Match output in Output.Matches(code))
            sendsValue[output.Groups["name"].Value] = output.Groups["arguments"].Value.Contains("Parameter =");

        var problems = new List<string>();
        foreach (Match call in FiredByName.Matches(code))
        {
            List<string> arguments = Arguments(call.Groups["arguments"].Value);
            foreach (string name in Named(code, arguments[0]))
            {
                if (!sendsValue.TryGetValue(name, out bool sends))
                    problems.Add($"{name} is not an output this class declares");
                else if (sends)
                    problems.Add($"{name} sends a value and is fired by name");
                else if (arguments.Count > 2)
                    problems.Add($"{name} is fired by name with a value");
            }
        }

        return problems;
    }

    // Split at the commas that are not inside a bracket of any kind.
    private static List<string> Arguments(string list)
    {
        var arguments = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] is '(' or '[' or '{')
                depth++;
            else if (list[i] is ')' or ']' or '}')
                depth--;
            else if (list[i] == ',' && depth == 0)
            {
                arguments.Add(list[start..i].Trim());
                start = i + 1;
            }
        }

        arguments.Add(list[start..].Trim());
        return arguments;
    }

    // The outputs a first argument can name: itself, or every member of the
    // list it indexes. A list that cannot be found is reported as it is.
    private static string[] Named(string code, string argument)
    {
        int index = argument.IndexOf('[');
        if (index < 0)
            return [argument];

        Match list = Regex.Match(code, Regex.Escape(argument[..index]) + @"\s*=\s*\[(?<names>[^\]]*)\]");
        return list.Success
            ? list.Groups["names"].Value.Split(
                ',', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries)
            : [argument];
    }

    // OnDone sends nothing and OnValue a number. Plain lists the first, and
    // Mixed lists both.
    private static string Thing(string statement) => $$"""
        public sealed partial class TestThing : Entity
        {
            [EntityOutput]
            public const string OnDone = nameof(OnDone);

            [EntityOutput(Parameter = KeyvalueType.Float)]
            public const string OnValue = nameof(OnValue);

            private static readonly string[] Plain = [OnDone];

            private static readonly string[] Mixed =
            [
                OnDone, OnValue,
            ];

            [EntityInput("Go")]
            private void Go(ref EntityInputContext context)
            {
                {{statement}}
            }
        }
        """;
}
