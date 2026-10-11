using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A built-in input says it takes a value when its method reads one, and
/// names the type the class reads it as.
/// </summary>
// Nothing checks a declared type against what a method does with the text, so
// this reads the sources. A class reads its parameter with a KeyvalueWire
// reader on context.Parameter, in the method or in one it hands the context to.
public sealed class EntityInputSourceTests
{
    private static readonly Regex Input = new(
        @"\[EntityInput\(""(?<name>\w+)""(?<arguments>[^\]]*)\)\]\s*" +
        @"private\s+void\s+\w+\(ref\s+EntityInputContext\s+context\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex LineComment = new(@"//[^\r\n]*", RegexOptions.CultureInvariant);

    // The types whose text has to be parsed, and the reader that parses each.
    // Text and a target name are used as they come.
    private static readonly (string Type, string Reader)[] Readers =
    [
        ("Bool", "TryParseBool"), ("Int", "TryParseInt"), ("Float", "TryParseFloat"),
        ("Vec2", "TryParseVec2"), ("Vec3", "TryParseVec3"), ("Vec4", "TryParseVec4"),
        ("Color", "TryParseColor"), ("Angles", "TryParseAngles"), ("NodeRef", "TryParseNodeRef"),
    ];

    [Fact]
    public void An_input_that_reads_its_parameter_says_it_takes_a_value_and_no_other_does()
    {
        var wrong = new List<string>();
        int inputs = 0;

        foreach (string file in EntitySources.Files())
        {
            foreach (DeclaredInput input in InputsOf(File.ReadAllText(file)))
            {
                inputs++;
                if (input.SaysParameter != input.ReadsParameter)
                {
                    wrong.Add(input.SaysParameter
                        ? $"{Path.GetFileName(file)}: {input.Name} says it takes a value and never reads one"
                        : $"{Path.GetFileName(file)}: {input.Name} reads its parameter and says it takes none");
                }
            }
        }

        inputs.ShouldBeGreaterThan(40);
        wrong.ShouldBeEmpty();
    }

    [Fact]
    public void Every_input_is_written_in_the_shape_these_tests_can_read()
    {
        // An input written another way would pass the others unread.
        foreach (string file in EntitySources.Files())
        {
            string code = LineComment.Replace(File.ReadAllText(file), "");

            InputsOf(code).Count.ShouldBe(
                Regex.Matches(code, @"\[EntityInput\b").Count,
                $"{Path.GetFileName(file)}: write an input as [EntityInput(\"Name\")] on " +
                "private void Name(ref EntityInputContext context)");
        }
    }

    [Fact]
    public void A_class_reads_its_parameters_as_the_types_its_inputs_say()
    {
        var wrong = new List<string>();

        foreach (string file in EntitySources.Files())
        {
            string code = LineComment.Replace(File.ReadAllText(file), "");
            List<DeclaredInput> inputs = InputsOf(code);

            foreach ((string type, string reader) in Readers)
            {
                bool says = inputs.Exists(input => input.Arguments.Contains($"KeyvalueType.{type}"));
                bool reads = code.Contains($"KeyvalueWire.{reader}(context.Parameter");
                if (says != reads)
                {
                    wrong.Add(says
                        ? $"{Path.GetFileName(file)}: an input says {type} and nothing calls {reader} on it"
                        : $"{Path.GetFileName(file)}: {reader} reads a parameter no input says is a {type}");
                }
            }
        }

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public void Only_a_class_that_handles_no_parameter_lets_an_input_leave_it_out()
    {
        foreach (string file in EntitySources.Files())
        {
            string code = LineComment.Replace(File.ReadAllText(file), "");
            bool says = InputsOf(code).Exists(input => input.Arguments.Contains("CanOmitParameter = true"));

            code.Contains("context.Parameter.Length == 0").ShouldBe(says, Path.GetFileName(file));
        }
    }

    [Theory]
    [InlineData("=> Start();", false)]
    [InlineData("=> PressIn(context.Activator);", false)]
    [InlineData("{\n    FireCount++;\n    FireOnTimer(context.Activator);\n}", false)]
    [InlineData("=> TryStore(ref context);", true)]
    [InlineData("{\n    if (TryRead(ref context, out float value))\n        Value = value;\n}", true)]
    [InlineData("{\n    int match = IndexOfCase(context.Parameter);\n    if (match < 0) { return; }\n}", true)]
    [InlineData("=>\n    Move(Amount(ref context), ref context);", true)]
    public void A_method_reads_its_parameter_when_it_touches_it_or_hands_the_context_on(string body, bool reads)
    {
        string source =
            "[EntityInput(\"Thing\")]\nprivate void Thing(ref EntityInputContext context) " + body +
            "\n\nprivate void Other(ref EntityInputContext context) => Use(context.Parameter);";

        InputsOf(source).ShouldHaveSingleItem().ReadsParameter.ShouldBe(reads);
    }

    [Theory]
    [InlineData("[EntityInput(\"Open\")]", false)]
    [InlineData("[EntityInput(\"SetValue\", Parameter = KeyvalueType.Float)]", true)]
    [InlineData("[EntityInput(\"Add\", Parameter = KeyvalueType.Float, CanOmitParameter = true)]", true)]
    public void An_input_says_a_parameter_when_its_attribute_names_a_type(string attribute, bool says)
    {
        string source = attribute + "\r\n    private void Go(ref EntityInputContext context) => Stop();";

        InputsOf(source).ShouldHaveSingleItem().SaysParameter.ShouldBe(says);
    }

    private static List<DeclaredInput> InputsOf(string source)
    {
        string code = LineComment.Replace(source, "");

        var inputs = new List<DeclaredInput>();
        foreach (Match match in Input.Matches(code))
        {
            string body = BodyAt(code, match.Index + match.Length);
            inputs.Add(new DeclaredInput(
                match.Groups["name"].Value,
                match.Groups["arguments"].Value,
                body.Contains("context.Parameter") || body.Contains("ref context")));
        }

        return inputs;
    }

    // An expression body up to its semicolon, or a block up to its last brace.
    private static string BodyAt(string code, int start)
    {
        int open = code.IndexOf('{', start);
        int arrow = code.IndexOf("=>", start, System.StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
            return code[arrow..(code.IndexOf(';', arrow) + 1)];

        int depth = 0;
        for (int i = open; i >= 0 && i < code.Length; i++)
        {
            if (code[i] == '{')
                depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[open..(i + 1)];
        }

        return "";
    }

    private sealed record DeclaredInput(string Name, string Arguments, bool ReadsParameter)
    {
        public bool SaysParameter => Arguments.Contains("Parameter = KeyvalueType.");
    }
}
